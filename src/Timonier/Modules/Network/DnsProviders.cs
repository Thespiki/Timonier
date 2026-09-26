using System.Net;
using System.Net.Sockets;
using Timonier.Core.Security;

namespace Timonier.Modules.Network;

/// <summary>Type de filtrage appliqué par un fournisseur DNS (affiché sous forme de badge).</summary>
internal enum DnsFiltering { None, Malware, AdsTrackers, Family }

/// <summary>
/// Fournisseur DNS public connu. Les adresses et le modèle DoH sont des CONSTANTES : c'est la liste blanche utilisée
/// par l'action admin <c>network.dns.set</c> (seule la clé du fournisseur circule vers le broker).
/// </summary>
internal sealed record DnsProvider(
    string Key,
    string Name,
    string Description,
    DnsFiltering Filtering,
    string[] IPv4,
    string[] IPv6,
    string? DohTemplate)
{
    public IEnumerable<string> All => IPv4.Concat(IPv6);
    public bool IsFamily => Filtering == DnsFiltering.Family;
}

internal static class DnsProviders
{
    public const string Auto = "auto";
    public const string Custom = "custom";

    public static readonly IReadOnlyList<DnsProvider> Known =
    [
        new("cloudflare", "Cloudflare",
            L("Fast, with no filtering. Cloudflare commits to not keeping your IP address for more than 25 hours (public audits)."),
            DnsFiltering.None,
            ["1.1.1.1", "1.0.0.1"], ["2606:4700:4700::1111", "2606:4700:4700::1001"],
            "https://cloudflare-dns.com/dns-query"),
        new("cloudflare-family", L("Cloudflare Family"),
            L("Blocks malicious sites and adult content (1.1.1.3). Designed for family or parental use."),
            DnsFiltering.Family,
            ["1.1.1.3", "1.0.0.3"], ["2606:4700:4700::1113", "2606:4700:4700::1003"],
            "https://family.cloudflare-dns.com/dns-query"),
        new("quad9", "Quad9",
            L("Swiss nonprofit foundation: blocks known malicious domains (phishing, malware) without logging IP addresses."),
            DnsFiltering.Malware,
            ["9.9.9.9", "149.112.112.112"], ["2620:fe::fe", "2620:fe::9"],
            "https://dns.quad9.net/dns-query"),
        new("google", "Google Public DNS",
            L("Reliable, with no filtering. Google keeps temporary logs (24 to 48 h) and aggregated data."),
            DnsFiltering.None,
            ["8.8.8.8", "8.8.4.4"], ["2001:4860:4860::8888", "2001:4860:4860::8844"],
            "https://dns.google/dns-query"),
        new("adguard", "AdGuard DNS",
            L("Blocks a large share of ads and trackers for all browsers and apps. May break some sites or sponsored links."),
            DnsFiltering.AdsTrackers,
            ["94.140.14.14", "94.140.15.15"], ["2a10:50c0::ad1:ff", "2a10:50c0::ad2:ff"],
            "https://dns.adguard-dns.com/dns-query"),
        new("adguard-family", L("AdGuard Family"),
            L("Blocks ads, trackers and adult content; enforces safe search (Google, Bing, YouTube Restricted Mode)."),
            DnsFiltering.Family,
            ["94.140.14.15", "94.140.15.16"], ["2a10:50c0::bad1:ff", "2a10:50c0::bad2:ff"],
            "https://family.adguard-dns.com/dns-query"),
        new("opendns-family", "OpenDNS FamilyShield",
            L("Cisco service that automatically blocks adult content, with no account to create."),
            DnsFiltering.Family,
            ["208.67.222.123", "208.67.220.123"], ["2620:119:35::123", "2620:119:53::123"],
            "https://doh.familyshield.opendns.com/dns-query"),
    ];

    public static DnsProvider? Get(string key) => Known.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Retrouve le fournisseur correspondant à une liste de serveurs (au moins un serveur connu, aucun inconnu).</summary>
    public static DnsProvider? Identify(IEnumerable<string> servers)
    {
        var list = servers.Select(Normalize).Where(s => s.Length > 0).ToList();
        if (list.Count == 0) return null;
        return Known.FirstOrDefault(p => list.All(s => p.All.Any(a => Normalize(a) == s)));
    }

    public static string FilteringLabel(DnsFiltering f) => f switch
    {
        DnsFiltering.Family => L("Family filter"),
        DnsFiltering.Malware => L("Blocks threats"),
        DnsFiltering.AdsTrackers => L("Blocks ads and trackers"),
        _ => L("No filtering"),
    };

    private static string Normalize(string ip) =>
        IPAddress.TryParse(ip.Trim(), out var a) ? a.ToString().ToLowerInvariant() : "";

    /// <summary>
    /// Analyse une saisie « personnalisée » : adresses séparées par des virgules, espaces ou points-virgules.
    /// Au plus 2 IPv4 et 2 IPv6 ; refuse les adresses non routables comme serveur (0.0.0.0, ::, multidiffusion, diffusion)
    /// et les identifiants de zone. Lève <see cref="ValidationException"/> avec un message clair.
    /// </summary>
    public static (List<string> V4, List<string> V6) ParseCustom(string input)
    {
        var parts = input.Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) throw new ValidationException(L("Enter at least one DNS server address."));
        if (parts.Length > 4) throw new ValidationException(L("Four addresses at most (2 IPv4 and 2 IPv6)."));
        var v4 = new List<string>();
        var v6 = new List<string>();
        foreach (var part in parts)
        {
            if (part.Contains('%')) throw new ValidationException(L("Zone ID not accepted: {0}", part));
            var ip = Validate.IpAddress(part);
            if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.Broadcast) || ip.Equals(IPAddress.None))
                throw new ValidationException(L("Address can't be used as a DNS server: {0}", part));
            if (ip.AddressFamily == AddressFamily.InterNetworkV6 ? ip.IsIPv6Multicast : (ip.GetAddressBytes()[0] & 0xF0) == 0xE0)
                throw new ValidationException(L("Multicast address rejected: {0}", part));
            var canonical = ip.ToString();
            var target = ip.AddressFamily == AddressFamily.InterNetwork ? v4 : v6;
            if (target.Contains(canonical, StringComparer.OrdinalIgnoreCase)) throw new ValidationException(L("Duplicate address: {0}", part));
            target.Add(canonical);
        }
        if (v4.Count > 2) throw new ValidationException(L("Two IPv4 addresses at most."));
        if (v6.Count > 2) throw new ValidationException(L("Two IPv6 addresses at most."));
        return (v4, v6);
    }
}
