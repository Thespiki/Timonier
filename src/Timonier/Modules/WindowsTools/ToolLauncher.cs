using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;
using Timonier.Core.Platform;

namespace Timonier.Modules.WindowsTools;

/// <summary>
/// Informations Windows lues une seule fois dans le Registre (lecture rapide, compatible avec <c>IModule.Register</c>).
/// </summary>
internal static class WinInfo
{
    private static readonly Lazy<(int Build, string Edition)> Info = new(Read);

    /// <summary>Numéro de build (ex. 26200). 22000 et plus = Windows 11.</summary>
    public static int Build => Info.Value.Build;

    /// <summary>EditionID (Core, Professional, Education, Enterprise…).</summary>
    public static string Edition => Info.Value.Edition;

    public static bool IsWindows11 => Build >= 22000;

    /// <summary>Éditions « Famille » : EditionID Core, CoreN, CoreSingleLanguage, CoreCountrySpecific.</summary>
    public static bool IsHome => Edition.StartsWith("Core", StringComparison.OrdinalIgnoreCase);

    public static bool IsProOrHigher => !IsHome;

    private static (int, string) Read()
    {
        var build = Environment.OSVersion.Version.Build;
        var edition = "";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key?.GetValue("CurrentBuild") is string s && int.TryParse(s, out var b)) build = b;
            edition = key?.GetValue("EditionID") as string ?? "";
        }
        catch (Exception ex)
        {
            Log.Warn("WindowsTools", "lecture de la version de Windows : " + ex.Message);
        }
        return (build, edition);
    }
}

/// <summary>
/// Lancement des outils Windows, toujours par chemin absolu et arguments constants (jamais de ligne de commande).
/// <para>Les outils dont le manifeste exige l'élévation (mmc.exe, regedit.exe, msconfig.exe…) ne peuvent pas être
/// démarrés par <c>CreateProcess</c> (erreur 740) : on repasse alors par ShellExecute, avec exactement le même
/// chemin absolu et les mêmes arguments, pour que Windows affiche lui-même l'invite UAC.</para>
/// </summary>
internal static class ToolLauncher
{
    private const int ErrorElevationRequired = 740;
    private const int ErrorCancelled = 1223;

    /// <summary>Exécutables de System32 absents de <see cref="SystemTool"/> que ce module peut ouvrir (liste blanche).</summary>
    private static readonly HashSet<string> System32Allowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        "dfrgui.exe", "optionalfeatures.exe", "SystemPropertiesComputerName.exe", "SystemPropertiesRemote.exe",
        "RecoveryDrive.exe", "msra.exe", "sndvol.exe", "dccw.exe", "cttune.exe", "mblctr.exe", "odbcad32.exe",
        "eudcedit.exe",
    };

    /// <summary>Console MMC de System32 (fichier .msc constant).</summary>
    public static string MscPath(string msc) => Path.Combine(Environment.SystemDirectory, msc);

    public static bool System32FileExists(string fileName) => File.Exists(Path.Combine(Environment.SystemDirectory, fileName));

    /// <summary>
    /// Lance un outil de la liste blanche du cœur. <see cref="ProcessRunner.Launch"/> gère lui-même l'erreur 740
    /// (relance via ShellExecute et invite UAC) : aucun repli supplémentaire ici.
    /// </summary>
    public static void Launch(SystemTool tool, params string[] args) => ProcessRunner.Launch(tool, args);

    /// <summary>Lance un exécutable de System32 figurant dans la liste blanche du module.</summary>
    public static void LaunchSystem32(string fileName, params string[] args)
    {
        if (!System32Allowlist.Contains(fileName) || fileName.IndexOfAny(['\\', '/', ':']) >= 0)
            throw new ArgumentException(L("Outil non autorisé : {0}", fileName));
        var path = Path.Combine(Environment.SystemDirectory, fileName);
        if (!File.Exists(path)) throw new FileNotFoundException(L("Outil système introuvable : {0}", fileName), path);
        var psi = new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = Environment.SystemDirectory };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            Process.Start(psi)?.Dispose();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorElevationRequired)
        {
            ElevatedLaunch(path, args);
        }
    }

    /// <summary>
    /// ShellExecute attend la réponse à l'invite UAC : on le fait hors du thread de l'interface pour ne pas la figer.
    /// </summary>
    private static void ElevatedLaunch(string path, string[] args)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
            throw new FileNotFoundException(L("Outil système introuvable : {0}", Path.GetFileName(path)), path);
        _ = Task.Run(() =>
        {
            try { ShellLaunch(path, args); }
            catch (Exception ex) { Log.Warn("WindowsTools", $"ouverture de {Path.GetFileName(path)} : {ex.Message}"); }
        });
    }

    /// <summary>Ouvre une console MMC de System32 (le nom est une constante du catalogue, jamais une saisie).</summary>
    public static void LaunchConsole(string msc)
    {
        if (!msc.EndsWith(".msc", StringComparison.OrdinalIgnoreCase) || msc.IndexOfAny(['\\', '/', ':']) >= 0)
            throw new ArgumentException(L("Console non autorisée : {0}", msc));
        var path = MscPath(msc);
        if (!File.Exists(path)) throw new FileNotFoundException(L("Console introuvable : {0}", msc), path);
        Launch(SystemTool.Mmc, path);
    }

    private static void ShellLaunch(string path, string[] args)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
            throw new FileNotFoundException(L("Outil système introuvable : {0}", Path.GetFileName(path)), path);
        // Même mise en forme des arguments que ProcessRunner.Launch (règles de CommandLineToArgvW) pour ShellExecute.
        var psi = new ProcessStartInfo(path, ProcessRunner.JoinArguments(args)) { UseShellExecute = true, WorkingDirectory = Environment.SystemDirectory };
        try
        {
            Log.Info("WindowsTools", $"ShellExecute {Path.GetFileName(path)} ({args.Length} arg.)");
            Process.Start(psi)?.Dispose();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            // L'utilisateur a refusé l'invite UAC : rien à signaler.
        }
    }
}
