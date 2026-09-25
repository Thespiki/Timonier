using System.Diagnostics;
using System.Text;
using Timonier.Core.Platform;

namespace Timonier.Broker;

/// <summary>
/// Auto-test du canal broker (compilé uniquement en Debug) : lance le broker SANS élévation, vérifie la poignée de
/// main, les refus d'identifiants inconnus et les contrôles d'identité, puis le ferme. N'applique aucun réglage.
/// Usage : Timonier.exe --selftest-broker  → résultat dans %LOCALAPPDATA%\Timonier\logs\selftest-broker.txt
/// </summary>
public static class BrokerSelfTest
{
    private const string EnvFlag = "TIMONIER_BROKER_SELFTEST";

    /// <summary>Vrai uniquement dans un build Debug lancé par l'auto-test.</summary>
    public static bool IsSelfTestBroker
    {
        get
        {
#if DEBUG
            return Environment.GetEnvironmentVariable(EnvFlag) == "1";
#else
            return false;
#endif
        }
    }

    internal static ProcessStartInfo UnelevatedStartInfo(string arguments)
    {
#if DEBUG
        var psi = new ProcessStartInfo(AppPaths.ExecutablePath, arguments) { UseShellExecute = false, CreateNoWindow = true };
        psi.Environment[EnvFlag] = "1";
        return psi;
#else
        throw new InvalidOperationException("Auto-test indisponible dans cette version.");
#endif
    }

    public static int Run()
    {
#if DEBUG
        return RunAsync().GetAwaiter().GetResult();
#else
        return 1;
#endif
    }

#if DEBUG
    private static async Task<int> RunAsync()
    {
        var report = new StringBuilder();
        var failures = 0;
        void Check(string name, bool ok, string detail = "")
        {
            report.AppendLine($"{(ok ? "OK  " : "FAIL")} {name}{(detail.Length > 0 ? " — " + detail : "")}");
            if (!ok) failures++;
        }

        var client = new BrokerClient { SelfTestWithoutElevation = true, IdleMinutes = 1 };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await client.EnsureConnectedAsync(timeout.Token);
            Check("Connexion + hello + vérification du PID serveur", client.IsRunning, $"pid broker {client.BrokerProcessId}");

            var r1 = await client.SendRawAsync(new BrokerRequest { Op = "apply", TweakId = "inexistant.tweak", Option = "on" }, timeout.Token);
            Check("Réglage inconnu refusé", !r1.Ok && r1.Message == "Réglage inconnu.", r1.Message ?? "");

            var r2 = await client.SendRawAsync(new BrokerRequest { Op = "apply", TweakId = "custom.explorer.extensions", Option = "bogus" }, timeout.Token);
            Check("Option inconnue refusée", !r2.Ok && r2.Message == "Option inconnue.", r2.Message ?? "");

            var r2b = await client.SendRawAsync(new BrokerRequest { Op = "apply", TweakId = "custom.explorer.extensions", Option = "on" }, timeout.Token);
            Check("Réglage non-admin refusé par le broker (moindre privilège)", !r2b.Ok && r2b.Message == "Ce réglage ne s'applique pas en mode administrateur.", r2b.Message ?? "");

            var r3 = await client.SendRawAsync(new BrokerRequest { Op = "action", ActionId = "inexistant.action", Params = [] }, timeout.Token);
            Check("Action inconnue refusée", !r3.Ok && r3.Message == "Action inconnue.", r3.Message ?? "");

            var r4 = await client.SendRawAsync(new BrokerRequest { Op = "undo", EntryId = Guid.NewGuid() }, timeout.Token);
            Check("Annulation d'une entrée inexistante refusée", !r4.Ok && r4.Message == "Entrée de journal introuvable.", r4.Message ?? "");

            var r5 = await client.SendRawAsync(new BrokerRequest { Op = "rm -rf" }, timeout.Token);
            Check("Opération inconnue refusée", !r5.Ok && r5.Message == "Opération inconnue.", r5.Message ?? "");

            var brokerPid = client.BrokerProcessId;
            await client.StopAsync();
            var exited = brokerPid is { } pid && WaitExit(pid, 5000);
            Check("Fermeture du broker sur demande", exited && !client.IsRunning);
        }
        catch (Exception ex)
        {
            Check("Session auto-test", false, ex.GetType().Name + ": " + ex.Message);
        }

        // Identité du parent : un broker lancé avec le PID d'un autre programme doit refuser de démarrer (code 4).
        try
        {
            var explorer = Process.GetProcessesByName("explorer").FirstOrDefault();
            if (explorer is not null)
            {
                var psi = UnelevatedStartInfo($"--broker Timonier.Broker.{Guid.NewGuid():N} {explorer.Id} 1");
                using var p = Process.Start(psi)!;
                p.WaitForExit(15000);
                Check("Parent usurpé refusé (code 4)", p.HasExited && p.ExitCode == 4, $"code {(p.HasExited ? p.ExitCode : -1)}");
            }
        }
        catch (Exception ex) { Check("Parent usurpé", false, ex.Message); }

        // Arguments invalides : code 2.
        try
        {
            using var p = Process.Start(UnelevatedStartInfo("--broker ../../evil 1 1"))!;
            p.WaitForExit(15000);
            Check("Arguments invalides refusés (code 2)", p.HasExited && p.ExitCode == 2, $"code {(p.HasExited ? p.ExitCode : -1)}");
        }
        catch (Exception ex) { Check("Arguments invalides", false, ex.Message); }

        // Langue inconnue ou mal formée (4e argument) : code 2 avant toute autre vérification.
        try
        {
            using var p = Process.Start(UnelevatedStartInfo($"--broker Timonier.Broker.{Guid.NewGuid():N} {Environment.ProcessId} 1 ..\\fr"))!;
            p.WaitForExit(15000);
            Check("Langue invalide refusée (code 2)", p.HasExited && p.ExitCode == 2, $"code {(p.HasExited ? p.ExitCode : -1)}");
        }
        catch (Exception ex) { Check("Langue invalide", false, ex.Message); }

        report.AppendLine(failures == 0 ? "RÉSULTAT : tous les tests passent" : $"RÉSULTAT : {failures} échec(s)");
        Directory.CreateDirectory(AppPaths.Logs);
        await File.WriteAllTextAsync(Path.Combine(AppPaths.Logs, "selftest-broker.txt"), report.ToString(), Encoding.UTF8);
        return failures == 0 ? 0 : 1;
    }

    private static bool WaitExit(int pid, int ms)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.WaitForExit(ms);
        }
        catch (ArgumentException) { return true; } // déjà terminé
    }
#endif
}
