using System.Runtime.InteropServices;
using System.Text;

namespace PcPilot.Modules.Network;

internal sealed record WlanInterface(Guid Id, string Description, int State)
{
    public bool IsConnected => State == 1;
}

internal sealed record WlanConnection(string ProfileName, string? Ssid, int SignalQuality, long RxRateKbps, bool Secured);

internal sealed record WlanProfile(Guid InterfaceId, string InterfaceDescription, string Name, int Flags)
{
    public bool IsGroupPolicy => (Flags & 1) != 0;
    public bool IsPerUser => (Flags & 2) != 0;
}

/// <summary>Résultat d'une requête WLAN : valeur, ou code d'erreur Win32 (5 = accès refusé, 1062 = service arrêté…).</summary>
internal readonly record struct WlanResult<T>(T? Value, uint Error)
{
    public bool Ok => Error == 0;
}

/// <summary>
/// Accès à l'API Wi-Fi native (wlanapi.dll), en lecture et pour supprimer un profil enregistré. Aucune connexion
/// réseau n'est initiée. Les mots de passe ne sont jamais lus (pas de WlanGetProfile avec clé en clair).
/// Depuis Windows 11 24H2, le nom du réseau connecté (SSID) exige l'autorisation « Localisation » : l'erreur 5 est
/// alors renvoyée et affichée comme telle.
/// </summary>
internal static partial class WlanApi
{
    public const uint ErrorAccessDenied = 5;
    public const uint ErrorServiceNotActive = 1062;
    public const uint ErrorNotFound = 1168;

    private const int InterfaceInfoSize = 16 + 512 + 4;
    private const int ProfileInfoSize = 512 + 4;
    private const int OpcodeCurrentConnection = 7;

    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanOpenHandle(uint clientVersion, nint reserved, out uint negotiatedVersion, out nint handle);

    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanCloseHandle(nint handle, nint reserved);

    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanEnumInterfaces(nint handle, nint reserved, out nint list);

    [LibraryImport("wlanapi.dll")]
    private static partial void WlanFreeMemory(nint memory);

    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanQueryInterface(nint handle, in Guid iface, int opCode, nint reserved, out int dataSize, out nint data, nint valueType);

    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanGetProfileList(nint handle, in Guid iface, nint reserved, out nint list);

    [LibraryImport("wlanapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint WlanDeleteProfile(nint handle, in Guid iface, string profileName, nint reserved);

    private static uint Open(out nint handle)
    {
        handle = 0;
        try { return WlanOpenHandle(2, 0, out _, out handle); }
        catch (DllNotFoundException) { return ErrorServiceNotActive; }   // Windows sans pile Wi-Fi (Server Core…)
        catch (EntryPointNotFoundException) { return ErrorServiceNotActive; }
    }

    public static WlanResult<List<WlanInterface>> Interfaces()
    {
        var err = Open(out var h);
        if (err != 0) return new(null, err);
        try
        {
            err = WlanEnumInterfaces(h, 0, out var list);
            if (err != 0) return new(null, err);
            try
            {
                var count = Marshal.ReadInt32(list);
                var result = new List<WlanInterface>(count);
                for (var i = 0; i < count; i++)
                {
                    var item = list + 8 + i * InterfaceInfoSize;
                    var guid = Marshal.PtrToStructure<Guid>(item);
                    var desc = Marshal.PtrToStringUni(item + 16, 256).TrimEnd('\0');
                    var state = Marshal.ReadInt32(item + 16 + 512);
                    result.Add(new WlanInterface(guid, desc, state));
                }
                return new(result, 0);
            }
            finally { WlanFreeMemory(list); }
        }
        finally { WlanCloseHandle(h, 0); }
    }

    /// <summary>Connexion en cours sur une interface (nom du profil, SSID, qualité du signal).</summary>
    public static WlanResult<WlanConnection> CurrentConnection(Guid iface)
    {
        var err = Open(out var h);
        if (err != 0) return new(null, err);
        try
        {
            err = WlanQueryInterface(h, iface, OpcodeCurrentConnection, 0, out var size, out var data, 0);
            if (err != 0) return new(null, err);
            try
            {
                if (size < 600) return new(null, ErrorNotFound);
                var profile = Marshal.PtrToStringUni(data + 8, 256).TrimEnd('\0');
                var ssidLength = Math.Clamp(Marshal.ReadInt32(data + 520), 0, 32);
                var ssidBytes = new byte[ssidLength];
                Marshal.Copy(data + 524, ssidBytes, 0, ssidLength);
                var ssid = ssidLength == 0 ? null : DecodeSsid(ssidBytes);
                var signal = Marshal.ReadInt32(data + 576);
                var rx = (uint)Marshal.ReadInt32(data + 580);
                var secured = Marshal.ReadInt32(data + 588) != 0;
                return new(new WlanConnection(profile, ssid, signal, rx, secured), 0);
            }
            finally { WlanFreeMemory(data); }
        }
        finally { WlanCloseHandle(h, 0); }
    }

    /// <summary>Profils Wi-Fi enregistrés sur toutes les interfaces (noms et type uniquement).</summary>
    public static WlanResult<List<WlanProfile>> Profiles()
    {
        var ifaces = Interfaces();
        if (!ifaces.Ok) return new(null, ifaces.Error);
        var err = Open(out var h);
        if (err != 0) return new(null, err);
        try
        {
            var result = new List<WlanProfile>();
            foreach (var iface in ifaces.Value!)
            {
                err = WlanGetProfileList(h, iface.Id, 0, out var list);
                if (err != 0) continue;
                try
                {
                    var count = Marshal.ReadInt32(list);
                    for (var i = 0; i < count; i++)
                    {
                        var item = list + 8 + i * ProfileInfoSize;
                        var name = Marshal.PtrToStringUni(item, 256).TrimEnd('\0');
                        var flags = Marshal.ReadInt32(item + 512);
                        result.Add(new WlanProfile(iface.Id, iface.Description, name, flags));
                    }
                }
                finally { WlanFreeMemory(list); }
            }
            return new(result, 0);
        }
        finally { WlanCloseHandle(h, 0); }
    }

    /// <summary>Supprime un profil (le mot de passe enregistré est effacé avec lui). Renvoie le code Win32.</summary>
    public static uint DeleteProfile(Guid iface, string profileName)
    {
        var err = Open(out var h);
        if (err != 0) return err;
        try { return WlanDeleteProfile(h, iface, profileName, 0); }
        finally { WlanCloseHandle(h, 0); }
    }

    private static string DecodeSsid(byte[] bytes)
    {
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }

    public static string Describe(uint error) => error switch
    {
        ErrorAccessDenied => "accès refusé par Windows",
        ErrorServiceNotActive => "service Wi-Fi (WLAN AutoConfig) inactif ou absent",
        ErrorNotFound => "élément introuvable",
        _ => "erreur " + error,
    };
}

/// <summary>Cache du résolveur DNS de Windows (dnsapi.dll). Ne nécessite pas de droits administrateur.</summary>
internal static partial class DnsCache
{
    [LibraryImport("dnsapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DnsFlushResolverCache();

    /// <summary>Vide le cache DNS ; en cas d'échec de l'API, se rabat sur « ipconfig /flushdns ».</summary>
    public static async Task<bool> FlushAsync()
    {
        try
        {
            if (DnsFlushResolverCache()) return true;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            // API non exportée sur cette version : on utilise l'outil système.
        }
        var r = await Core.Platform.ProcessRunner.RunAsync(Core.Platform.SystemTool.IpConfig, ["/flushdns"],
            new Core.Platform.RunOptions { Timeout = TimeSpan.FromSeconds(20) }).ConfigureAwait(false);
        return r.Success;
    }
}
