using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;
using Timonier.Core.Localization;
using Timonier.Core.Platform;
using WinConnectivity = Windows.Networking.Connectivity;

namespace Timonier.Modules.Network;

internal enum AdapterKind { Ethernet, Wifi, Mobile, Vpn, Virtual, Other }

/// <summary>Instantané d'une carte réseau (lecture seule, aucune requête sur le réseau).</summary>
internal sealed class AdapterInfo
{
    public required string Id { get; init; }
    public Guid Guid { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public AdapterKind Kind { get; init; }
    public bool IsUp { get; init; }
    public long SpeedBps { get; init; }
    public int IfIndex { get; init; }
    public string Mac { get; init; } = "";
    public List<string> IPv4 { get; init; } = [];
    public List<string> IPv6 { get; init; } = [];
    public List<string> Gateways { get; init; } = [];
    public List<string> DnsServers { get; init; } = [];
    public List<string> StaticDns4 { get; init; } = [];
    public List<string> StaticDns6 { get; init; } = [];
    public bool DhcpEnabled { get; init; }
    public string? DhcpServer { get; init; }
    public string? DnsSuffix { get; init; }
    public bool SupportsIPv6 { get; init; }

    public bool HasGateway => Gateways.Count > 0;
    public bool DnsIsManual => StaticDns4.Count > 0 || StaticDns6.Count > 0;
    public bool IsPhysical => Kind is AdapterKind.Ethernet or AdapterKind.Wifi or AdapterKind.Mobile;

    /// <summary>Carte éligible au changement de DNS : active, avec un index d'interface valide.</summary>
    public bool CanSetDns => IsUp && IfIndex > 0 && Kind != AdapterKind.Other;

    public string KindLabel => Kind switch
    {
        AdapterKind.Wifi => "Wi-Fi",
        AdapterKind.Ethernet => "Ethernet",
        AdapterKind.Mobile => L("Réseau mobile"),
        AdapterKind.Vpn => L("VPN / tunnel"),
        AdapterKind.Virtual => L("Carte virtuelle"),
        _ => L("Autre"),
    };

    public string Glyph => Kind switch
    {
        AdapterKind.Wifi => "",
        AdapterKind.Ethernet => "",
        AdapterKind.Mobile => "",
        AdapterKind.Vpn => "",
        _ => "",
    };

    public string SpeedLabel => SpeedBps switch
    {
        <= 0 => "—",
        >= 1_000_000_000 => (SpeedBps / 1_000_000_000d).ToString("0.#", Loc.Culture) + " Gbit/s",
        >= 1_000_000 => (SpeedBps / 1_000_000d).ToString("0.#", Loc.Culture) + " Mbit/s",
        _ => (SpeedBps / 1000d).ToString("0", Loc.Culture) + " kbit/s",
    };

    /// <summary>MAC au format AA:BB:CC:DD:EE:FF, masquée (les 3 derniers octets identifient la carte).</summary>
    public string MacDisplay(bool reveal)
    {
        if (Mac.Length != 12) return "—";
        var pairs = Enumerable.Range(0, 6).Select(i => Mac.Substring(i * 2, 2)).ToArray();
        return reveal ? string.Join(":", pairs) : string.Join(":", pairs.Take(3)) + ":••:••:••";
    }

    public string DnsSummary
    {
        get
        {
            if (DnsServers.Count == 0) return L("Aucun serveur DNS");
            var provider = DnsProviders.Identify(DnsServers);
            return provider is not null ? provider.Name : string.Join(", ", DnsServers.Take(2));
        }
    }
}

internal enum ConnectivityLevel { Unknown, None, LocalAccess, ConstrainedInternet, Internet }

/// <summary>État de connectivité tel que Windows le connaît déjà (NCSI) : Timonier n'envoie aucune requête.</summary>
internal sealed record ConnectivityInfo(ConnectivityLevel Level, string? ProfileName, bool IsWlan, bool IsMetered);

internal static class NetworkInfo
{
    /// <summary>Énumère les cartes réseau. <paramref name="includeHidden"/> : inclut boucle locale et tunnels système.</summary>
    public static List<AdapterInfo> ReadAdapters(bool includeHidden = false)
    {
        var result = new List<AdapterInfo>();
        NetworkInterface[] all;
        try { all = NetworkInterface.GetAllNetworkInterfaces(); }
        catch (NetworkInformationException ex)
        {
            Log.Warn("Network", "énumération des cartes impossible : " + ex.Message);
            return result;
        }

        foreach (var ni in all)
        {
            if (!includeHidden && ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            try { result.Add(Read(ni)); }
            catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException or InvalidOperationException)
            {
                Log.Warn("Network", $"carte {ni.Name} illisible : {ex.Message}");
            }
        }

        // Ordre : actives d'abord, avec passerelle, puis physiques.
        return [.. result
            .OrderByDescending(a => a.IsUp)
            .ThenByDescending(a => a.HasGateway)
            .ThenByDescending(a => a.IsPhysical)
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static AdapterInfo Read(NetworkInterface ni)
    {
        var props = ni.GetIPProperties();
        int index = 0;
        bool dhcp = false;
        try
        {
            var v4 = props.GetIPv4Properties();
            if (v4 is not null) { index = v4.Index; dhcp = v4.IsDhcpEnabled; }
        }
        catch (NetworkInformationException) { /* IPv4 désactivé sur la carte */ }
        var supportsV6 = ni.Supports(NetworkInterfaceComponent.IPv6);
        if (index == 0 && supportsV6)
        {
            try { index = props.GetIPv6Properties()?.Index ?? 0; }
            catch (NetworkInformationException) { }
        }

        var uni = props.UnicastAddresses;
        var ipv4 = uni.Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork)
                      .Select(u => u.PrefixLength > 0 ? $"{u.Address}/{u.PrefixLength}" : u.Address.ToString()).ToList();
        var ipv6 = uni.Where(u => u.Address.AddressFamily == AddressFamily.InterNetworkV6)
                      .OrderBy(u => u.Address.IsIPv6LinkLocal)
                      .ThenBy(u => u.SuffixOrigin == SuffixOrigin.Random ? 0 : 1)
                      .Select(u => StripScope(u.Address)).Distinct().ToList();
        var gateways = props.GatewayAddresses
            .Select(g => g.Address)
            .Where(a => !a.Equals(IPAddress.Any) && !a.Equals(IPAddress.IPv6Any))
            .Select(StripScope).Distinct().ToList();
        var dns = props.DnsAddresses
            .Where(a => !(a.AddressFamily == AddressFamily.InterNetworkV6 && a.IsIPv6SiteLocal && a.ToString().StartsWith("fec0:0:0:ffff", StringComparison.OrdinalIgnoreCase)))
            .Select(StripScope).Distinct().ToList();
        string? dhcpServer = null;
        try { dhcpServer = props.DhcpServerAddresses.FirstOrDefault()?.ToString(); } catch (PlatformNotSupportedException) { }

        Guid.TryParse(ni.Id, out var guid);
        return new AdapterInfo
        {
            Id = ni.Id,
            Guid = guid,
            Name = ni.Name,
            Description = ni.Description,
            Kind = Classify(ni),
            IsUp = ni.OperationalStatus == OperationalStatus.Up,
            SpeedBps = ni.OperationalStatus == OperationalStatus.Up ? ni.Speed : 0,
            IfIndex = index,
            Mac = ni.GetPhysicalAddress().ToString(),
            IPv4 = ipv4,
            IPv6 = ipv6,
            Gateways = gateways,
            DnsServers = dns,
            StaticDns4 = ReadStaticDns(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + ni.Id),
            StaticDns6 = ReadStaticDns(@"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\Interfaces\" + ni.Id),
            DhcpEnabled = dhcp,
            DhcpServer = dhcp ? dhcpServer : null,
            DnsSuffix = string.IsNullOrWhiteSpace(props.DnsSuffix) ? null : props.DnsSuffix,
            SupportsIPv6 = supportsV6,
        };
    }

    private static string StripScope(IPAddress a)
    {
        var s = a.ToString();
        var i = s.IndexOf('%');
        return i > 0 ? s[..i] : s;
    }

    private static AdapterKind Classify(NetworkInterface ni)
    {
        var d = ni.Description;
        bool Has(string s) => d.Contains(s, StringComparison.OrdinalIgnoreCase);
        if (ni.NetworkInterfaceType is NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2) return AdapterKind.Mobile;
        if (ni.NetworkInterfaceType is NetworkInterfaceType.Ppp or NetworkInterfaceType.Tunnel ||
            Has("vpn") || Has("wireguard") || Has("tap-windows") || Has("wintun") || Has("openvpn") || Has("tailscale") || Has("zerotier") ||
            Has("anyconnect") || Has("fortinet") || Has("globalprotect"))
            return AdapterKind.Vpn;
        // Pseudo-cartes système (WAN Miniport, Wi-Fi Direct, débogage noyau…) : jamais utiles à l'utilisateur.
        // Modules de filtrage NDIS (« …-Native WiFi Filter Driver-0000 », « …-QoS Packet Scheduler-0000 », WFP…) :
        // .NET les énumère comme des interfaces distinctes, mais ce ne sont que des couches de la carte réelle.
        if (Has("miniport") || Has("kernel debug") || Has("teredo") || Has("isatap") || Has("6to4") || Has("wi-fi direct") ||
            Has("filter driver") || Has("packet scheduler") || Has("lightweight filter") || Has("wfp ") ||
            d.EndsWith("-0000", StringComparison.Ordinal) || ni.Name.EndsWith("-0000", StringComparison.Ordinal))
            return AdapterKind.Other;
        if (ni.NetworkInterfaceType is NetworkInterfaceType.Wireless80211) return AdapterKind.Wifi;
        if (Has("hyper-v") || Has("virtual") || Has("vmware") || Has("virtualbox") || Has("loopback") || Has("wsl") || Has("vethernet") ||
            ni.Name.StartsWith("vEthernet", StringComparison.OrdinalIgnoreCase))
            return AdapterKind.Virtual;
        if (ni.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit)
            return Has("bluetooth") ? AdapterKind.Other : AdapterKind.Ethernet;
        return AdapterKind.Other;
    }

    /// <summary>Serveurs DNS saisis manuellement (valeur NameServer) ; vide = attribués automatiquement.</summary>
    internal static List<string> ReadStaticDns(string key)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(key);
            if (k?.GetValue("NameServer") is not string s || string.IsNullOrWhiteSpace(s)) return [];
            return [.. s.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    /// <summary>Carte « principale » : active, avec passerelle, physique de préférence.</summary>
    public static AdapterInfo? Primary(IEnumerable<AdapterInfo> adapters) =>
        adapters.Where(a => a.IsUp && a.HasGateway).OrderByDescending(a => a.IsPhysical).FirstOrDefault()
        ?? adapters.FirstOrDefault(a => a.IsUp && a.IsPhysical);

    /// <summary>Niveau de connectivité connu de Windows (API WinRT, sans trafic réseau de la part de Timonier).</summary>
    public static ConnectivityInfo ReadConnectivity()
    {
        try
        {
            var profile = WinConnectivity.NetworkInformation.GetInternetConnectionProfile();
            if (profile is null) return new ConnectivityInfo(ConnectivityLevel.None, null, false, false);
            var level = profile.GetNetworkConnectivityLevel() switch
            {
                WinConnectivity.NetworkConnectivityLevel.InternetAccess => ConnectivityLevel.Internet,
                WinConnectivity.NetworkConnectivityLevel.ConstrainedInternetAccess => ConnectivityLevel.ConstrainedInternet,
                WinConnectivity.NetworkConnectivityLevel.LocalAccess => ConnectivityLevel.LocalAccess,
                _ => ConnectivityLevel.None,
            };
            string? name = null;
            try { name = profile.ProfileName; } catch (Exception) { /* nom masqué */ }
            var metered = false;
            try
            {
                var cost = profile.GetConnectionCost();
                metered = cost.NetworkCostType is WinConnectivity.NetworkCostType.Fixed or WinConnectivity.NetworkCostType.Variable;
            }
            catch (Exception) { /* coût inconnu */ }
            return new ConnectivityInfo(level, string.IsNullOrWhiteSpace(name) ? null : name, profile.IsWlanConnectionProfile, metered);
        }
        catch (Exception ex)
        {
            Log.Warn("Network", "connectivité WinRT indisponible : " + ex.Message);
            return new ConnectivityInfo(ConnectivityLevel.Unknown, null, false, false);
        }
    }

    public static string ConnectivityLabel(ConnectivityLevel level) => level switch
    {
        ConnectivityLevel.Internet => L("Connecté à Internet"),
        ConnectivityLevel.ConstrainedInternet => L("Accès limité (portail de connexion ?)"),
        ConnectivityLevel.LocalAccess => L("Réseau local uniquement"),
        ConnectivityLevel.None => L("Aucune connexion"),
        _ => L("État inconnu"),
    };
}
