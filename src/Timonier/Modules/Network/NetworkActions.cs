using Timonier.Core.Catalog;
using Timonier.Core.Model;
using Timonier.Core.Platform;
using Timonier.Core.Security;

namespace Timonier.Modules.Network;

/// <summary>Identifiants des actions du module (constantes partagées entre la page et les gestionnaires).</summary>
internal static class NetworkActionIds
{
    public const string SetDns = "network.dns.set";
    public const string FlushDns = "network.dns.flush";
    public const string HostsAdd = "network.hosts.add";
    public const string HostsRemove = "network.hosts.remove";
    public const string HostsClear = "network.hosts.clear";
    public const string RenewIp = "network.ip.renew";
    public const string Reset = "network.reset";
    public const string WifiForget = "network.wifi.forget";
    public const string WifiForgetAdmin = "network.wifi.forget.admin";
}

/// <summary>
/// Change les serveurs DNS d'une carte (admin). Paramètres : <c>ifIndex</c> (validé contre les cartes actuelles),
/// <c>provider</c> (liste blanche) ou <c>custom</c> + <c>servers</c> (IP validées), <c>doh</c>, <c>ipv6</c>.
/// Renvoie la configuration précédente dans Data (<c>prevProvider</c>, <c>prevServers</c>) pour permettre l'annulation.
/// </summary>
internal sealed class SetDnsAction : IActionHandler
{
    private const string Script = """
        $i = [int]$env:TMN_IF
        # Remise à zéro d'abord : Set-DnsClientServerAddress ne touche qu'aux familles (IPv4/IPv6) présentes dans la liste,
        # d'anciens serveurs IPv6 fixes resteraient sinon actifs à côté des nouveaux serveurs IPv4.
        Set-DnsClientServerAddress -InterfaceIndex $i -ResetServerAddresses
        if ($env:TMN_MODE -ne 'reset') {
            $a = @($env:TMN_SERVERS.Split(',') | Where-Object { $_ })
            try {
                Set-DnsClientServerAddress -InterfaceIndex $i -ServerAddresses $a
                if ($env:TMN_DOH -eq '1') {
                    foreach ($s in $a) {
                        $e = $null
                        try { $e = Get-DnsClientDohServerAddress -ServerAddress $s -ErrorAction Stop } catch { $e = $null }
                        if ($e) { Set-DnsClientDohServerAddress -ServerAddress $s -DohTemplate $env:TMN_TEMPLATE -AllowFallbackToUdp $true -AutoUpgrade $true | Out-Null }
                        else { Add-DnsClientDohServerAddress -ServerAddress $s -DohTemplate $env:TMN_TEMPLATE -AllowFallbackToUdp $true -AutoUpgrade $true | Out-Null }
                    }
                }
            } catch {
                # Échec : la configuration précédente est rétablie (rien ne doit rester à moitié appliqué).
                $err = $_
                if ($env:TMN_PREV) {
                    $p = @($env:TMN_PREV.Split(',') | Where-Object { $_ })
                    try { Set-DnsClientServerAddress -InterfaceIndex $i -ServerAddresses $p } catch { }
                }
                throw $err
            }
        }
        Clear-DnsClientCache
        'OK'
        """;

    public string Id => NetworkActionIds.SetDns;
    public string Title => "Changer les serveurs DNS";
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        Validate.Int(p, "ifIndex", 1, 16_777_215);
        var provider = Validate.OneOf(p, "provider", [DnsProviders.Auto, DnsProviders.Custom, .. DnsProviders.Known.Select(k => k.Key)]);
        Validate.Bool(p, "ipv6", true);
        var doh = Validate.Bool(p, "doh");
        if (provider == DnsProviders.Custom)
        {
            DnsProviders.ParseCustom(Validate.Required(p, "servers", 200));
            if (doh) throw new ValidationException("Le DNS chiffré n'est proposé que pour les fournisseurs connus.");
        }
        else if (Validate.Optional(p, "servers", 200) is not null)
        {
            throw new ValidationException("Paramètre « servers » réservé au mode personnalisé.");
        }
        if (doh && provider == DnsProviders.Auto) throw new ValidationException("Le DNS chiffré nécessite un fournisseur connu.");
        if (doh && Environment.OSVersion.Version.Build < 22000) throw new ValidationException("Le DNS chiffré (DoH) nécessite Windows 11.");
    }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var ifIndex = Validate.Int(p, "ifIndex", 1, 16_777_215);
        var providerKey = Validate.OneOf(p, "provider", [DnsProviders.Auto, DnsProviders.Custom, .. DnsProviders.Known.Select(k => k.Key)]);
        var doh = Validate.Bool(p, "doh");

        // L'index doit désigner une carte active existante (jamais une valeur arbitraire).
        var adapter = NetworkInfo.ReadAdapters(includeHidden: true).FirstOrDefault(a => a.IfIndex == ifIndex);
        if (adapter is null || !adapter.CanSetDns) return ActionResult.Fail("Carte réseau introuvable ou inactive. Actualisez la liste et réessayez.");
        var ipv6 = Validate.Bool(p, "ipv6", true) && adapter.SupportsIPv6;

        List<string> servers;
        DnsProvider? provider = null;
        if (providerKey == DnsProviders.Custom)
        {
            var (v4, v6) = DnsProviders.ParseCustom(Validate.Required(p, "servers", 200));
            servers = [.. v4, .. v6];
        }
        else if (providerKey == DnsProviders.Auto)
        {
            servers = [];
        }
        else
        {
            provider = DnsProviders.Get(providerKey)!;
            servers = [.. provider.IPv4, .. ipv6 ? provider.IPv6 : []];
        }

        var previous = adapter.StaticDns4.Concat(adapter.StaticDns6).ToList();
        var previousProvider = previous.Count == 0 ? DnsProviders.Auto : DnsProviders.Identify(previous)?.Key ?? DnsProviders.Custom;

        ctx.Progress?.Report($"Configuration des DNS de « {adapter.Name} »…");
        var env = new Dictionary<string, string>
        {
            ["IF"] = ifIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["MODE"] = providerKey == DnsProviders.Auto ? "reset" : "set",
            ["SERVERS"] = string.Join(",", servers),
            ["DOH"] = doh && provider?.DohTemplate is not null ? "1" : "0",
            ["TEMPLATE"] = provider?.DohTemplate ?? "",
            // Adresses fixes actuelles (lues dans le registre par le broker), rétablies si l'application échoue.
            ["PREV"] = string.Join(",", previous.Where(s => System.Net.IPAddress.TryParse(s, out _))),
        };
        var r = await PowerShellRunner.RunAsync(Script, env, TimeSpan.FromSeconds(60), ct: ctx.Cancellation).ConfigureAwait(false);
        if (!r.Success)
        {
            Log.Warn("Network", "dns.set : " + r.Error.Trim());
            return ActionResult.Fail("Windows a refusé la modification des DNS : " + FirstLine(r.Error));
        }

        var label = providerKey switch
        {
            DnsProviders.Auto => "DNS automatiques (fournis par le réseau)",
            DnsProviders.Custom => "DNS personnalisés : " + string.Join(", ", servers),
            _ => provider!.Name + (env["DOH"] == "1" ? " (chiffré)" : ""),
        };
        Log.Info("Network", $"DNS de l'interface {ifIndex} : {providerKey}");
        return ActionResult.Ok($"« {adapter.Name} » utilise maintenant : {label}.", new Dictionary<string, string>
        {
            ["ifIndex"] = env["IF"],
            ["prevProvider"] = previousProvider,
            ["prevServers"] = string.Join(",", previous),
        });
    }

    internal static string FirstLine(string text)
    {
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "erreur inconnue";
        return line.Length > 200 ? line[..200] + "…" : line;
    }
}

/// <summary>Vide le cache du résolveur DNS (sans droits administrateur).</summary>
internal sealed class FlushDnsAction : IActionHandler
{
    public string Id => NetworkActionIds.FlushDns;
    public string Title => "Vider le cache DNS";
    public bool RequiresAdmin => false;
    public void ValidateParameters(IReadOnlyDictionary<string, string> p) { }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p) =>
        await DnsCache.FlushAsync().ConfigureAwait(false)
            ? ActionResult.Ok("Cache DNS vidé : les noms de sites seront de nouveau résolus.")
            : ActionResult.Fail("Impossible de vider le cache DNS.");
}

/// <summary>Bloque un site via la section Timonier du fichier hosts (admin).</summary>
internal sealed class HostsAddAction : IActionHandler
{
    public string Id => NetworkActionIds.HostsAdd;
    public string Title => "Bloquer un site (fichier hosts)";
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        HostsFile.ValidateBlockHost(Validate.Required(p, "host", 260));
        Validate.Bool(p, "www", true);
    }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var host = HostsFile.ValidateBlockHost(Validate.Required(p, "host", 260));
        var withWww = Validate.Bool(p, "www", true) && !host.StartsWith("www.", StringComparison.Ordinal);
        var snapshot = HostsFile.Read();
        if (snapshot.Error is not null) return ActionResult.Fail("Fichier hosts illisible : " + snapshot.Error);

        var managed = HostsWrite.ValidManaged(snapshot);
        var toAdd = new List<string> { host };
        if (withWww) toAdd.Add("www." + host);
        toAdd = [.. toAdd.Where(h => !managed.Contains(h))];
        if (toAdd.Count == 0) return ActionResult.Ok($"« {host} » est déjà bloqué.");
        if (managed.Count + toAdd.Count > HostsFile.MaxManagedEntries)
            return ActionResult.Fail($"Limite de {HostsFile.MaxManagedEntries} entrées atteinte : retirez d'abord des sites bloqués.");

        if (HostsWrite.Try([.. managed, .. toAdd]) is { } error) return error;
        await DnsCache.FlushAsync().ConfigureAwait(false);
        return ActionResult.Ok($"« {host} » est bloqué sur ce PC{(withWww ? " (avec www)" : "")}.");
    }
}

/// <summary>Retire un site de la section Timonier du fichier hosts (admin).</summary>
internal sealed class HostsRemoveAction : IActionHandler
{
    public string Id => NetworkActionIds.HostsRemove;
    public string Title => "Débloquer un site (fichier hosts)";
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) =>
        Validate.HostName(Validate.Required(p, "host", 260));

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var host = Validate.HostName(Validate.Required(p, "host", 260));
        var snapshot = HostsFile.Read();
        if (snapshot.Error is not null) return ActionResult.Fail("Fichier hosts illisible : " + snapshot.Error);
        // Seules les entrées de la section Timonier peuvent être retirées.
        if (!snapshot.Managed.Any(e => e.Host == host)) return ActionResult.Fail($"« {host} » ne fait pas partie des sites bloqués par Timonier.");
        var managed = HostsWrite.ValidManaged(snapshot);
        managed.Remove(host);
        if (HostsWrite.Try(managed) is { } error) return error;
        await DnsCache.FlushAsync().ConfigureAwait(false);
        return ActionResult.Ok($"« {host} » est de nouveau accessible.");
    }
}

/// <summary>Supprime toute la section Timonier du fichier hosts (admin).</summary>
internal sealed class HostsClearAction : IActionHandler
{
    public string Id => NetworkActionIds.HostsClear;
    public string Title => "Retirer tous les blocages Timonier du fichier hosts";
    public bool RequiresAdmin => true;
    public void ValidateParameters(IReadOnlyDictionary<string, string> p) { }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        var snapshot = HostsFile.Read();
        if (snapshot.Error is not null) return ActionResult.Fail("Fichier hosts illisible : " + snapshot.Error);
        var count = snapshot.Managed.Count();
        if (count == 0) return ActionResult.Ok("Aucun site bloqué par Timonier.");
        if (HostsWrite.Try([]) is { } error) return error;
        await DnsCache.FlushAsync().ConfigureAwait(false);
        return ActionResult.Ok($"{count} entrée(s) retirée(s) du fichier hosts. Les autres lignes n'ont pas été modifiées.");
    }
}

/// <summary>Écriture de la section Timonier du fichier hosts avec des messages d'échec compréhensibles.</summary>
internal static class HostsWrite
{
    /// <summary>
    /// Sites de la section Timonier qui passent encore la validation. Une ligne modifiée à la main dans la section, ou un
    /// domaine devenu protégé depuis, est abandonnée à la prochaine écriture au lieu de bloquer tout ajout ou retrait.
    /// </summary>
    public static List<string> ValidManaged(HostsSnapshot snapshot)
    {
        var result = new List<string>();
        foreach (var host in snapshot.Managed.Select(e => e.Host).Distinct())
        {
            try
            {
                if (HostsFile.ValidateBlockHost(host) == host) result.Add(host);
            }
            catch (ValidationException)
            {
                Log.Warn("Network", "hosts : entrée ignorée dans la section Timonier : " + host);
            }
        }
        return result;
    }

    /// <summary>Réécrit la section ; renvoie null si tout s'est bien passé, sinon le résultat d'échec à renvoyer.</summary>
    public static ActionResult? Try(IReadOnlyList<string> hosts)
    {
        try
        {
            HostsFile.WriteManaged(hosts);
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ActionResult.Fail(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Network", "hosts : écriture impossible : " + ex.Message);
            return ActionResult.Fail("Impossible de modifier le fichier hosts : il est peut-être verrouillé ou protégé par un logiciel de " +
                                     "sécurité. Détail : " + ex.Message);
        }
    }
}

/// <summary>Libère puis renouvelle les adresses IP obtenues par DHCP (ipconfig, admin).</summary>
internal sealed class RenewIpAction : IActionHandler
{
    public string Id => NetworkActionIds.RenewIp;
    public string Title => "Renouveler l'adresse IP";
    public bool RequiresAdmin => true;
    public void ValidateParameters(IReadOnlyDictionary<string, string> p) { }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ctx.Progress?.Report("Libération des adresses IP…");
        var release = await ProcessRunner.RunAsync(SystemTool.IpConfig, ["/release"], new RunOptions { Timeout = TimeSpan.FromSeconds(60) }, ctx.Cancellation).ConfigureAwait(false);
        ctx.Progress?.Report("Demande de nouvelles adresses…");
        var renew = await ProcessRunner.RunAsync(SystemTool.IpConfig, ["/renew"], new RunOptions { Timeout = TimeSpan.FromSeconds(120) }, ctx.Cancellation).ConfigureAwait(false);
        await DnsCache.FlushAsync().ConfigureAwait(false);
        if (renew.Success) return ActionResult.Ok("Adresses IP renouvelées. La connexion peut mettre quelques secondes à revenir.");
        if (renew.TimedOut) return ActionResult.Fail("Le serveur DHCP (box, routeur) n'a pas répondu à temps. Vérifiez la connexion puis réessayez.");
        var detail = SetDnsAction.FirstLine(string.IsNullOrWhiteSpace(renew.Error) ? renew.Output : renew.Error);
        return ActionResult.Fail((release.Success ? "" : "Libération incomplète. ") + "Renouvellement impossible : " + detail);
    }
}

/// <summary>Réinitialise Winsock et la pile TCP/IP (admin, redémarrage requis, confirmation élevée).</summary>
internal sealed class NetworkResetAction : IActionHandler
{
    public string Id => NetworkActionIds.Reset;
    public string Title => "Réinitialiser la pile réseau";
    public bool RequiresAdmin => true;
    public bool RequiresElevatedConfirmation => true;

    public string DescribeForConfirmation(IReadOnlyDictionary<string, string> parameters) =>
        "Timonier va réinitialiser le catalogue Winsock et la configuration TCP/IP de Windows " +
        "(netsh winsock reset, netsh int ip reset).\n\n" +
        "Les adresses IP et DNS saisies manuellement seront effacées et certains logiciels réseau (VPN, pare-feu tiers, " +
        "proxy) devront peut-être être réinstallés ou reconfigurés. Un redémarrage est nécessaire.\n\nContinuer ?";

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) { }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        var options = new RunOptions { Timeout = TimeSpan.FromSeconds(90) };
        ctx.Progress?.Report("Réinitialisation de Winsock…");
        var winsock = await ProcessRunner.RunAsync(SystemTool.Netsh, ["winsock", "reset"], options, ctx.Cancellation).ConfigureAwait(false);
        ctx.Progress?.Report("Réinitialisation de TCP/IP…");
        var ip = await ProcessRunner.RunAsync(SystemTool.Netsh, ["int", "ip", "reset"], options, ctx.Cancellation).ConfigureAwait(false);
        await DnsCache.FlushAsync().ConfigureAwait(false);
        Log.Info("Network", $"réinitialisation réseau : winsock={winsock.ExitCode} ip={ip.ExitCode}");

        if (!winsock.Success && !ip.Success)
            return ActionResult.Fail("La réinitialisation a échoué : " + SetDnsAction.FirstLine(winsock.CombinedOutput));
        var partial = winsock.Success && ip.Success ? "" : " (partielle : une des deux commandes a signalé une erreur)";
        return new ActionResult(true, $"Pile réseau réinitialisée{partial}. Redémarrez le PC pour terminer.") { Effect = ApplyEffect.Reboot };
    }
}

/// <summary>Oublie un réseau Wi-Fi enregistré (profil et mot de passe associé). Version sans élévation.</summary>
internal class WifiForgetAction : IActionHandler
{
    public virtual string Id => NetworkActionIds.WifiForget;
    public string Title => "Oublier un réseau Wi-Fi";
    public virtual bool RequiresAdmin => false;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        Validate.Guid(p, "iface");
        var name = Validate.Required(p, "profile", 256);
        if (name.Any(char.IsControl)) throw new ValidationException("Nom de profil invalide.");
    }

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var iface = Validate.Guid(p, "iface");
        // Nom exact (non « trimé ») : doit exister dans l'énumération actuelle.
        var name = p["profile"];
        var profiles = WlanApi.Profiles();
        if (!profiles.Ok) return Task.FromResult(ActionResult.Fail("Liste des réseaux Wi-Fi indisponible : " + WlanApi.Describe(profiles.Error)));
        var profile = profiles.Value!.FirstOrDefault(x => x.InterfaceId == iface && x.Name == name);
        if (profile is null) return Task.FromResult(ActionResult.Fail("Ce réseau n'est plus enregistré sur ce PC."));
        if (profile.IsGroupPolicy) return Task.FromResult(ActionResult.Fail("Ce réseau est imposé par une stratégie de l'organisation : il ne peut pas être supprimé ici."));

        var err = WlanApi.DeleteProfile(iface, name);
        if (err == 0) return Task.FromResult(ActionResult.Ok($"Réseau « {name} » oublié. Son mot de passe devra être saisi à la prochaine connexion."));
        return Task.FromResult(new ActionResult(false, "Suppression impossible : " + WlanApi.Describe(err))
        {
            Data = new Dictionary<string, string> { ["error"] = err.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        });
    }
}

/// <summary>Même action, exécutée par le broker (profils « tous les utilisateurs » protégés).</summary>
internal sealed class WifiForgetAdminAction : WifiForgetAction
{
    public override string Id => NetworkActionIds.WifiForgetAdmin;
    public override bool RequiresAdmin => true;
}
