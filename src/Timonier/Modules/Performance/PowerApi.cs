using System.Runtime.InteropServices;
using Timonier.Core.Platform;

namespace Timonier.Modules.Performance;

/// <summary>Plan d'alimentation énuméré par powrprof.</summary>
public sealed record PowerScheme(Guid Id, string Name, bool IsActive, SchemePersonality Personality)
{
    public bool IsBalanced => Id == PowerApi.Balanced || Personality == SchemePersonality.Balanced;
    public bool IsHighPerformance => Personality == SchemePersonality.HighPerformance || Id == PowerApi.HighPerformance || Id == PowerApi.UltimateTemplate;
    public bool IsPowerSaver => Personality == SchemePersonality.PowerSaver || Id == PowerApi.PowerSaver;
}

/// <summary>Personnalité d'un plan (paramètre GUID_POWERSCHEME_PERSONALITY) : famille dont il dérive.</summary>
public enum SchemePersonality { Unknown = -1, PowerSaver = 0, HighPerformance = 1, Balanced = 2 }

/// <summary>Mode d'alimentation de Windows 10/11 (superposition appliquée au plan « Utilisation normale »).</summary>
public sealed record PowerMode(string Key, Guid Id, string Label, string ShortLabel, string Glyph);

/// <summary>État électrique instantané (GetSystemPowerStatus).</summary>
public sealed record PowerSource(bool OnBattery, bool HasBattery, int? BatteryPercent, bool BatterySaver);

/// <summary>Minuteries « écran » et « veille » du plan actif, en secondes (0 = jamais).</summary>
public sealed record PowerTimeouts(int? MonitorAc, int? MonitorDc, int? SleepAc, int? SleepDc);

/// <summary>
/// Accès en lecture/écriture aux plans d'alimentation via l'API documentée de powrprof.dll (pas de ligne de commande).
/// Toutes les méthodes sont synchrones et rapides, mais à appeler hors du thread UI (appels RPC au service Power).
/// </summary>
public static partial class PowerApi
{
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    /// <summary>Modèle masqué « Performances optimales » (Ultimate Performance), à dupliquer pour l'utiliser.</summary>
    public static readonly Guid UltimateTemplate = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    // Modes d'alimentation (Paramètres > Système > Alimentation > Mode d'alimentation).
    public static readonly PowerMode ModeEfficiency = new("efficiency", new("961cc777-2547-4f9d-8174-7d86181b8a7a"),
        "Meilleure efficacité énergétique", "Économie", "");
    public static readonly PowerMode ModeBalanced = new("balanced", Guid.Empty, "Équilibré", "Équilibré", "");
    public static readonly PowerMode ModeBetterPerformance = new("better", new("3af9b8d9-7c97-431d-ad78-34a8bfea439f"),
        "Performances améliorées", "Performances+", "");
    public static readonly PowerMode ModePerformance = new("performance", new("ded574b5-45a0-4f42-8737-46345c09c238"),
        "Meilleures performances", "Performances", "");

    /// <summary>Modes proposés par Timonier (ceux de Windows 11).</summary>
    public static readonly PowerMode[] Modes = [ModeEfficiency, ModeBalanced, ModePerformance];

    public static PowerMode? ModeFromGuid(Guid g) =>
        g == ModeEfficiency.Id ? ModeEfficiency
        : g == ModeBalanced.Id ? ModeBalanced
        : g == ModeBetterPerformance.Id ? ModeBetterPerformance
        : g == ModePerformance.Id ? ModePerformance
        : null;

    public static PowerMode? ModeFromKey(string key) => Modes.FirstOrDefault(m => m.Key == key);

    private static readonly Guid NoSubgroup = new("fea3413e-7e05-4911-9a71-700331f1c294");
    private static readonly Guid Personality = new("245d8541-3943-4422-b025-13a784f679b7");
    private static readonly Guid SubVideo = new("7516b95f-f776-4464-8c53-06167f40cc99");
    private static readonly Guid VideoIdle = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");
    private static readonly Guid SubSleep = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
    private static readonly Guid StandbyIdle = new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");

    private const uint AccessScheme = 16;
    private const uint ErrorNoMoreItems = 259;
    public const uint ErrorAccessDenied = 5;

    // ------------------------------------------------------------------ Plans

    public static List<PowerScheme> EnumerateSchemes()
    {
        var active = GetActiveScheme();
        var result = new List<PowerScheme>();
        for (uint i = 0; i < 64; i++)
        {
            var buffer = new byte[16];
            uint size = 16;
            var rc = PowerEnumerate(0, 0, 0, AccessScheme, i, buffer, ref size);
            if (rc == ErrorNoMoreItems) break;
            if (rc != 0) break;
            var id = new Guid(buffer);
            result.Add(new PowerScheme(id, ReadFriendlyName(id) ?? id.ToString(), id == active, ReadPersonality(id)));
        }
        return result;
    }

    public static Guid? GetActiveScheme()
    {
        if (PowerGetActiveScheme(0, out var ptr) != 0 || ptr == 0) return null;
        try { return Marshal.PtrToStructure<Guid>(ptr); }
        finally { LocalFree(ptr); }
    }

    /// <summary>Active un plan. Renvoie le code d'erreur Win32 (0 = succès).</summary>
    public static uint SetActiveScheme(Guid scheme) => PowerSetActiveScheme(0, ref scheme);

    public static string? ReadFriendlyName(Guid scheme)
    {
        uint size = 0;
        if (PowerReadFriendlyName(0, ref scheme, 0, 0, null, ref size) != 0 || size == 0 || size > 4096) return null;
        var buffer = new byte[size];
        if (PowerReadFriendlyName(0, ref scheme, 0, 0, buffer, ref size) != 0) return null;
        return System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0').Trim();
    }

    private static SchemePersonality ReadPersonality(Guid scheme)
    {
        try
        {
            var sub = NoSubgroup;
            var setting = Personality;
            return PowerReadACValueIndex(0, ref scheme, ref sub, ref setting, out var value) == 0 && value <= 2
                ? (SchemePersonality)value
                : SchemePersonality.Unknown;
        }
        catch { return SchemePersonality.Unknown; }
    }

    // ------------------------------------------------------------------ Minuteries

    public static PowerTimeouts ReadTimeouts()
    {
        if (GetActiveScheme() is not { } scheme) return new PowerTimeouts(null, null, null, null);
        return new PowerTimeouts(
            Read(scheme, SubVideo, VideoIdle, ac: true), Read(scheme, SubVideo, VideoIdle, ac: false),
            Read(scheme, SubSleep, StandbyIdle, ac: true), Read(scheme, SubSleep, StandbyIdle, ac: false));
    }

    private static int? Read(Guid scheme, Guid subgroup, Guid setting, bool ac)
    {
        var rc = ac
            ? PowerReadACValueIndex(0, ref scheme, ref subgroup, ref setting, out var v)
            : PowerReadDCValueIndex(0, ref scheme, ref subgroup, ref setting, out v);
        return rc == 0 && v <= int.MaxValue ? (int)v : null;
    }

    // ------------------------------------------------------------------ Mode d'alimentation (superposition)

    /// <summary>Mode effectivement appliqué en ce moment (null si l'API est absente).</summary>
    public static Guid? GetEffectiveMode()
    {
        try { return PowerGetEffectiveOverlayScheme(out var g) == 0 ? g : null; }
        catch (EntryPointNotFoundException) { return null; }
    }

    /// <summary>API documentée des modes choisis par l'utilisateur (Windows 11) : disponible ?</summary>
    public static bool UserModeApiAvailable
    {
        get
        {
            try
            {
                var lib = NativeLibrary.Load("powrprof.dll");
                return NativeLibrary.TryGetExport(lib, "PowerGetUserConfiguredACPowerMode", out _)
                    && NativeLibrary.TryGetExport(lib, "PowerSetUserConfiguredACPowerMode", out _)
                    && NativeLibrary.TryGetExport(lib, "PowerSetUserConfiguredDCPowerMode", out _);
            }
            catch { return false; }
        }
    }

    public static Guid? GetUserMode(bool ac)
    {
        try
        {
            var rc = ac ? PowerGetUserConfiguredACPowerMode(out var g) : PowerGetUserConfiguredDCPowerMode(out g);
            return rc == 0 ? g : null;
        }
        catch (EntryPointNotFoundException) { return null; }
    }

    /// <summary>Définit le mode choisi pour le secteur ou la batterie. Renvoie le code d'erreur Win32 (0 = succès).</summary>
    public static uint SetUserMode(bool ac, Guid mode) =>
        ac ? PowerSetUserConfiguredACPowerMode(ref mode) : PowerSetUserConfiguredDCPowerMode(ref mode);

    // ------------------------------------------------------------------ Alimentation électrique

    public static PowerSource GetSource()
    {
        if (!GetSystemPowerStatus(out var s)) return new PowerSource(false, false, null, false);
        var noBattery = (s.BatteryFlag & 128) != 0 || s.BatteryFlag == 255;
        return new PowerSource(
            OnBattery: s.ACLineStatus == 0,
            HasBattery: !noBattery,
            BatteryPercent: s.BatteryLifePercent <= 100 && !noBattery ? s.BatteryLifePercent : null,
            BatterySaver: s.SystemStatusFlag == 1);
    }

    /// <summary>Hibernation activée (lecture seule du registre, sans admin).</summary>
    public static bool HibernationEnabled()
    {
        const string key = @"SYSTEM\CurrentControlSet\Control\Power";
        var v = RegistryAccess.ReadDword(Core.Model.RegHive.LocalMachine, key, "HibernateEnabled")
                ?? RegistryAccess.ReadDword(Core.Model.RegHive.LocalMachine, key, "HibernateEnabledDefault");
        return v != 0;
    }

    // ------------------------------------------------------------------ P/Invoke

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemPowerStatus(out SystemPowerStatus status);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint mem);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerEnumerate(nint rootPowerKey, nint schemeGuid, nint subGroupGuid, uint accessFlags, uint index,
        [Out] byte[]? buffer, ref uint bufferSize);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerReadFriendlyName(nint rootPowerKey, ref Guid schemeGuid, nint subGroupGuid, nint settingGuid,
        [Out] byte[]? buffer, ref uint bufferSize);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerGetActiveScheme(nint userRootPowerKey, out nint activePolicyGuid);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSetActiveScheme(nint userRootPowerKey, ref Guid schemeGuid);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerReadACValueIndex(nint rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid settingGuid, out uint value);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerReadDCValueIndex(nint rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid settingGuid, out uint value);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerGetEffectiveOverlayScheme(out Guid overlay);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerGetUserConfiguredACPowerMode(out Guid mode);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerGetUserConfiguredDCPowerMode(out Guid mode);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSetUserConfiguredACPowerMode(ref Guid mode);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSetUserConfiguredDCPowerMode(ref Guid mode);
}
