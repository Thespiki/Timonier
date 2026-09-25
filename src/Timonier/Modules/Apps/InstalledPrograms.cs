using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Timonier.Core.Model;
using Timonier.Core.Platform;

namespace Timonier.Modules.Apps;

/// <summary>Programme de bureau inscrit dans les clés « Uninstall » du registre.</summary>
public sealed record InstalledProgram(string DisplayName, string? Publisher, string? Version, long SizeBytes, DateTime? InstallDate,
    string? ProductCode, bool PerUser, bool Is32Bit, bool NoRemove)
{
    public string ScopeLabel => PerUser ? "Cet utilisateur" : "Tous les utilisateurs";
}

/// <summary>
/// Lecture seule des programmes installés (HKLM 64 bits, HKLM WOW6432Node, HKCU). Les composants système
/// (SystemComponent=1), les mises à jour (ParentKeyName, ReleaseType) et les entrées sans nom sont ignorés.
/// Utilisable dans l'interface comme dans le broker (HKCU de l'utilisateur via son SID).
/// </summary>
public static partial class InstalledPrograms
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    [GeneratedRegex(@"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$")]
    private static partial Regex ProductCodeRx();

    public static bool IsProductCode(string value) => ProductCodeRx().IsMatch(value);

    /// <param name="userSid">SID de l'utilisateur pour HKCU (broker) ; null = utilisateur courant.</param>
    public static List<InstalledProgram> Read(string? userSid = null)
    {
        var result = new List<InstalledProgram>();
        ReadHive(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64), perUser: false, is32: false, result);
        ReadHive(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32), perUser: false, is32: true, result);
        try
        {
            using var cu = userSid is null
                ? RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64)
                : RegistryAccess.OpenRoot(RegHive.CurrentUser, userSid, writable: false);
            ReadHive(cu, perUser: true, is32: false, result, dispose: false);
        }
        catch (Exception ex) { Log.Warn("Apps", "lecture HKCU\\Uninstall : " + ex.Message); }

        // Un même programme peut être inscrit deux fois (32/64 bits, machine/utilisateur) : on garde la première.
        return [.. result
            .GroupBy(p => (p.DisplayName.ToLowerInvariant(), p.Version))
            .Select(g => g.First())
            .OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static void ReadHive(RegistryKey root, bool perUser, bool is32, List<InstalledProgram> result, bool dispose = true)
    {
        try
        {
            using var uninstall = root.OpenSubKey(UninstallKey);
            if (uninstall is null) return;
            foreach (var name in uninstall.GetSubKeyNames())
            {
                try
                {
                    using var k = uninstall.OpenSubKey(name);
                    if (k is null) continue;
                    if (k.GetValue("DisplayName") is not string display || string.IsNullOrWhiteSpace(display)) continue;
                    if (k.GetValue("SystemComponent") is int sc && sc == 1) continue;
                    if (k.GetValue("ParentKeyName") is string parent && parent.Length > 0) continue;
                    if (k.GetValue("ReleaseType") is string rt && rt is "Update" or "Hotfix" or "Security Update" or "Service Pack") continue;

                    long size = k.GetValue("EstimatedSize") is int kb && kb > 0 ? kb * 1024L : 0;
                    DateTime? date = k.GetValue("InstallDate") is string d &&
                                     DateTime.TryParseExact(d.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                        ? dt : null;
                    var msi = k.GetValue("WindowsInstaller") is int wi && wi == 1 && IsProductCode(name);
                    var noRemove = k.GetValue("NoRemove") is int nr && nr == 1
                                   || (k.GetValue("UninstallString") is null && k.GetValue("QuietUninstallString") is null && !msi);
                    result.Add(new InstalledProgram(display.Trim(), (k.GetValue("Publisher") as string)?.Trim(),
                        (k.GetValue("DisplayVersion") as string)?.Trim(), size, date, msi ? name.ToUpperInvariant() : null,
                        perUser, is32, noRemove));
                }
                catch (Exception ex) { Log.Warn("Apps", $"entrée {name} : {ex.Message}"); }
            }
        }
        catch (Exception ex) { Log.Warn("Apps", "lecture Uninstall : " + ex.Message); }
        finally
        {
            if (dispose) root.Dispose();
        }
    }
}
