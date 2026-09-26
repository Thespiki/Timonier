using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Timonier.Core.Platform;

/// <summary>
/// Liste blanche des exécutables que Timonier a le droit de lancer. Les chemins sont résolus de façon absolue
/// (System32 / Windows) : impossible de détourner l'exécution via le PATH ou le dossier courant.
/// </summary>
public enum SystemTool
{
    PowerCfg, IpConfig, Netsh, PnpUtil, Sfc, Dism, SchTasks, CleanMgr, WsReset, BcdEdit, Shutdown, PowerShell,
    Winget, Explorer, Control, Mmc, Rundll32, Rstrui, TaskKill, ReAgentC, W32tm, Wevtutil, Defrag, Compact, Fsutil,
    Net, Systeminfo, Driverquery, Gpupdate, Dsregcmd, Msinfo32, Resmon, Perfmon, Taskmgr, Regedit, Msconfig,
    Magnify, Osk, Narrator, Winver, Dxdiag, Mdsched, SystemPropertiesAdvanced, SystemPropertiesProtection,
    SystemPropertiesPerformance, Netplwiz, Mstsc, Charmap, Chkdsk, Manage_bde, Vssadmin, Reg, Sc, Cmd, Notepad,
}

public static class SystemTools
{
    public static string FileName(SystemTool tool) => tool switch
    {
        SystemTool.Manage_bde => "manage-bde.exe",
        SystemTool.Winget => "winget.exe",
        _ => tool.ToString().ToLowerInvariant() + ".exe",
    };

    /// <summary>Chemin absolu de l'outil ; lève une exception s'il est introuvable.</summary>
    public static string Resolve(SystemTool tool)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var system32 = Environment.SystemDirectory;
        string path = tool switch
        {
            SystemTool.Explorer or SystemTool.Regedit => Path.Combine(windows, FileName(tool)),
            SystemTool.PowerShell => Path.Combine(system32, @"WindowsPowerShell\v1.0\powershell.exe"),
            SystemTool.Winget => ResolveWinget(),
            _ => Path.Combine(system32, FileName(tool)),
        };
        if (!File.Exists(path)) throw new FileNotFoundException(L("Outil système introuvable : {0}", FileName(tool)), path);
        return path;
    }

    public static bool IsAvailable(SystemTool tool)
    {
        try { Resolve(tool); return true; }
        catch { return false; }
    }

    private static string ResolveWinget()
    {
        // Alias d'exécution de l'App Installer (par utilisateur). Son dossier (%LOCALAPPDATA%\Microsoft\WindowsApps) est
        // modifiable sans élévation : un processus élevé ne l'utilise JAMAIS (un programme pourrait y remplacer
        // winget.exe et le faire exécuter avec les droits administrateur). Il prend l'exécutable du paquet installé
        // dans Program Files\WindowsApps, protégé par le système.
        var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe");
        if (ChildEnvironment.IsElevated)
            return ResolveWingetPackage() ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"WindowsApps\winget.exe");
        if (File.Exists(alias)) return alias;
        return ResolveWingetPackage() ?? alias;
    }

    /// <summary>winget.exe du paquet Microsoft.DesktopAppInstaller le plus récent (architecture du système), sinon null.</summary>
    private static string? ResolveWingetPackage()
    {
        var apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
        var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";
        try
        {
            string? best = null;
            Version? bestVersion = null;
            foreach (var dir in Directory.EnumerateDirectories(apps, $"Microsoft.DesktopAppInstaller_*_{arch}__8wekyb3d8bbwe"))
            {
                var parts = Path.GetFileName(dir).Split('_');
                if (parts.Length < 2 || !Version.TryParse(parts[1], out var version)) continue;
                if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) continue;
                var exe = Path.Combine(dir, "winget.exe");
                if (!File.Exists(exe) || (bestVersion is not null && version <= bestVersion)) continue;
                best = exe;
                bestVersion = version;
            }
            return best;
        }
        catch { return null; /* accès refusé à WindowsApps : normal hors élévation */ }
    }
}

public sealed record ProcessResult(int ExitCode, string Output, string Error, bool TimedOut)
{
    public bool Success => ExitCode == 0 && !TimedOut;
    public string CombinedOutput => string.IsNullOrWhiteSpace(Error) ? Output : Output + Environment.NewLine + Error;
}

public sealed class RunOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);
    public IProgress<string>? LineProgress { get; init; }
    /// <summary>Encodage de la sortie console. Par défaut : page de code OEM (ex. 850 en France).</summary>
    public Encoding? OutputEncoding { get; init; }
    /// <summary>Variables d'environnement transmises (canal de données sûr vers les scripts).</summary>
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
    public string? WorkingDirectory { get; init; }
}

/// <summary>
/// Lancement de processus SANS shell : chaque argument est passé séparément (ArgumentList), jamais concaténé
/// dans une chaîne interprétée. C'est la règle anti-injection n°1 de Timonier.
/// </summary>
public static class ProcessRunner
{
    static ProcessRunner()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Encoding OemEncoding
    {
        get
        {
            try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
            catch { return Encoding.UTF8; }
        }
    }

    public static Task<ProcessResult> RunAsync(SystemTool tool, IEnumerable<string> args, RunOptions? options = null, CancellationToken ct = default) =>
        RunPathAsync(SystemTools.Resolve(tool), args, options, ct);

    public static Task<ProcessResult> RunAsync(SystemTool tool, params string[] args) =>
        RunPathAsync(SystemTools.Resolve(tool), args, null, CancellationToken.None);

    /// <summary>
    /// Exécute un binaire par chemin absolu. Réservé aux chemins issus de la liste blanche ou validés
    /// par <see cref="Security.Validate.ExistingLocalFile"/> ; ne jamais passer une chaîne brute saisie par l'utilisateur.
    /// </summary>
    public static async Task<ProcessResult> RunPathAsync(string exePath, IEnumerable<string> args, RunOptions? options, CancellationToken ct)
    {
        options ??= new RunOptions();
        if (!Path.IsPathFullyQualified(exePath)) throw new ArgumentException("Chemin d'exécutable non absolu refusé.", nameof(exePath));

        var psi = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = options.OutputEncoding ?? OemEncoding,
            StandardErrorEncoding = options.OutputEncoding ?? OemEncoding,
            WorkingDirectory = options.WorkingDirectory ?? Environment.SystemDirectory,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        ChildEnvironment.Harden(psi.Environment);
        if (options.Environment is not null)
            foreach (var (k, v) in options.Environment) psi.Environment[k] = v;

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stdout) stdout.AppendLine(e.Data);
            if (e.Data.Length > 0) options.LineProgress?.Report(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stderr) stderr.AppendLine(e.Data);
        };

        Log.Info("Process", $"{Path.GetFileName(exePath)} ({psi.ArgumentList.Count} arg.)");
        process.Start();
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(options.Timeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = !ct.IsCancellationRequested;
            try { process.Kill(entireProcessTree: true); } catch { /* déjà terminé */ }
            if (ct.IsCancellationRequested) throw;
        }

        string o, e2;
        lock (stdout) o = stdout.ToString();
        lock (stderr) e2 = stderr.ToString();
        return new ProcessResult(timedOut ? -1 : process.ExitCode, o, e2, timedOut);
    }

    /// <summary>
    /// Lance un outil graphique (panneau de configuration, MMC…) sans attendre, sans interpréteur de commandes.
    /// Un outil dont le manifeste exige l'élévation (mmc, regedit, msconfig…) refuse CreateProcess (erreur 740) :
    /// il est alors relancé via ShellExecute, qui affiche l'invite UAC, avec le même chemin absolu et les mêmes
    /// arguments, sur un fil d'arrière-plan pour ne pas figer l'interface pendant l'invite.
    /// </summary>
    public static void Launch(SystemTool tool, params string[] args)
    {
        var path = SystemTools.Resolve(tool);
        var psi = new ProcessStartInfo(path) { UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        ChildEnvironment.Harden(psi.Environment);
        try
        {
            Process.Start(psi)?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 740)
        {
            var elevated = new ProcessStartInfo(path, JoinArguments(args)) { UseShellExecute = true };
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { Process.Start(elevated)?.Dispose(); }
                catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223) { } // UAC refusée
                catch (Exception e) { Log.Warn("Process", $"lancement de {Path.GetFileName(path)} : {e.Message}"); }
            });
        }
    }

    /// <summary>Ligne d'arguments Windows équivalente à ArgumentList (règles de CommandLineToArgvW).</summary>
    internal static string JoinArguments(IEnumerable<string> args)
    {
        var sb = new StringBuilder();
        foreach (var a in args)
        {
            if (sb.Length > 0) sb.Append(' ');
            if (a.Length > 0 && a.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0) { sb.Append(a); continue; }
            sb.Append('"');
            var backslashes = 0;
            foreach (var c in a)
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"') { sb.Append('\\', backslashes * 2 + 1); sb.Append('"'); }
                else { sb.Append('\\', backslashes); sb.Append(c); }
                backslashes = 0;
            }
            sb.Append('\\', backslashes * 2);
            sb.Append('"');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Ouvre une page des Paramètres Windows. Seul le schéma ms-settings: (et quelques schémas système) est accepté.
    /// </summary>
    public static void OpenSettingsUri(string uri)
    {
        if (!Regex.IsMatch(uri, @"^(ms-settings|windowsdefender|ms-windows-store|ms-availablenetworks|ms-actioncenter):[A-Za-z0-9\-_./?=&]{0,200}\z"))
            throw new ArgumentException(L("URI non autorisée : {0}", uri));
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
    }

    /// <summary>Domaines officiels que l'application peut ouvrir dans le navigateur (sous-domaines compris).</summary>
    private static readonly string[] OfficialDomains =
        ["microsoft.com", "windows.com", "nvidia.com", "amd.com", "intel.com", "intel.fr", "github.com"];

    /// <summary>
    /// Ouvre une page web officielle dans le navigateur par défaut : https uniquement, sans identifiants ni port,
    /// hôte limité à <see cref="OfficialDomains"/>. L'application elle-même ne se connecte jamais à ces sites.
    /// </summary>
    public static void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort
            || uri.UserInfo.Length > 0 || !OfficialDomains.Any(d => uri.IdnHost.Equals(d, StringComparison.OrdinalIgnoreCase)
                || uri.IdnHost.EndsWith("." + d, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException(L("Adresse non autorisée : {0}", url));
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
    }

    /// <summary>Ouvre un dossier local existant dans l'Explorateur.</summary>
    public static void OpenFolder(string path)
    {
        var full = Path.GetFullPath(path);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException(full);
        // ShellExecute sur le dossier lui-même (verbe « open ») : l'Explorateur ne réanalyse pas une ligne de commande,
        // où une virgule dans le chemin serait prise pour un séparateur d'options.
        Process.Start(new ProcessStartInfo(full) { UseShellExecute = true, Verb = "open" })?.Dispose();
    }
}

/// <summary>
/// Environnement des processus enfants. Un processus élevé via l'UAC reçoit les variables de HKCU\Environment, que
/// n'importe quel programme non élevé de la session peut modifier : profileur .NET (COR_PROFILER, CORECLR_…), crochets
/// de démarrage (DOTNET_STARTUP_HOOKS), modules PowerShell (PSModulePath), PATH… Elles permettraient d'injecter du code
/// dans les outils lancés avec les droits administrateur. On les retire toujours, et dans un processus élevé on remet
/// les chemins système à leur valeur d'origine (dossiers connus, jamais lus depuis l'environnement).
/// </summary>
internal static class ChildEnvironment
{
    private static readonly string[] InjectionPrefixes = ["COR_", "CORECLR_", "COMPLUS_", "DOTNET_"];

    private static readonly Lazy<bool> Elevated = new(() =>
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    });

    /// <summary>Processus courant élevé (broker) : aucun exécutable ni chemin issu du profil utilisateur.</summary>
    public static bool IsElevated => Elevated.Value;

    public static void Harden(IDictionary<string, string?> environment)
    {
        foreach (var name in environment.Keys.Where(k => InjectionPrefixes.Any(p => k.StartsWith(p, StringComparison.OrdinalIgnoreCase))).ToList())
            environment.Remove(name);
        if (!Elevated.Value) return;

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var system32 = Environment.SystemDirectory;
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        environment["SystemRoot"] = windows;
        environment["windir"] = windows;
        environment["ComSpec"] = Path.Combine(system32, "cmd.exe");
        environment["PATH"] = string.Join(';', system32, windows, Path.Combine(system32, "Wbem"),
            Path.Combine(system32, @"WindowsPowerShell\v1.0"), Path.Combine(system32, "OpenSSH"));
        environment["PSModulePath"] = string.Join(';', Path.Combine(programFiles, @"WindowsPowerShell\Modules"),
            Path.Combine(system32, @"WindowsPowerShell\v1.0\Modules"));
    }
}

/// <summary>
/// Exécution de scripts PowerShell CONSTANTS. Les données variables ne sont jamais insérées dans le texte du
/// script : elles sont passées en variables d'environnement TMN_* (lues via $env:TMN_NOM), ce qui rend
/// l'injection de code impossible par construction.
/// </summary>
public static partial class PowerShellRunner
{
    [GeneratedRegex("^[A-Z][A-Z0-9_]{0,30}$")]
    private static partial Regex ParamName();

    public static Task<ProcessResult> RunAsync(string constantScript, IReadOnlyDictionary<string, string>? parameters = null,
        TimeSpan? timeout = null, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var env = new Dictionary<string, string>();
        if (parameters is not null)
        {
            foreach (var (k, v) in parameters)
            {
                if (!ParamName().IsMatch(k)) throw new ArgumentException("Nom de paramètre PowerShell invalide : " + k);
                if (v.Length > 8192 || v.Contains('\0')) throw new ArgumentException(L("Valeur de paramètre refusée : {0}", k));
                env["TMN_" + k] = v;
            }
        }

        // Modules chargés uniquement depuis les dossiers système : PowerShell 5.1 ajoute sinon le dossier Documents de
        // l'utilisateur, modifiable sans élévation (chargement automatique d'un module piégé dans un script élevé).
        const string prelude = "$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';" +
                               "$env:PSModulePath=[IO.Path]::Combine([Environment]::GetFolderPath('ProgramFiles'),'WindowsPowerShell\\Modules')+';'+" +
                               "[IO.Path]::Combine([Environment]::GetFolderPath('System'),'WindowsPowerShell\\v1.0\\Modules');" +
                               "[Console]::OutputEncoding=[Text.Encoding]::UTF8;";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(prelude + constantScript));
        return ProcessRunner.RunAsync(SystemTool.PowerShell,
            ["-NoProfile", "-NonInteractive", "-NoLogo", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded],
            new RunOptions
            {
                Timeout = timeout ?? TimeSpan.FromMinutes(3),
                OutputEncoding = Encoding.UTF8,
                Environment = env,
                LineProgress = progress,
            }, ct);
    }
}
