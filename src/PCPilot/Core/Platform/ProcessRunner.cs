using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PcPilot.Core.Platform;

/// <summary>
/// Liste blanche des exécutables que PC Pilot a le droit de lancer. Les chemins sont résolus de façon absolue
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
        if (!File.Exists(path)) throw new FileNotFoundException($"Outil système introuvable : {FileName(tool)}", path);
        return path;
    }

    public static bool IsAvailable(SystemTool tool)
    {
        try { Resolve(tool); return true; }
        catch { return false; }
    }

    private static string ResolveWinget()
    {
        // Alias d'exécution de l'App Installer (par utilisateur).
        var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe");
        if (File.Exists(alias)) return alias;
        // Processus élevé par un autre compte : on cherche l'installation système du DesktopAppInstaller.
        var apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
        try
        {
            var dir = Directory.EnumerateDirectories(apps, "Microsoft.DesktopAppInstaller_*_x64__8wekyb3d8bbwe")
                               .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (dir is not null && File.Exists(Path.Combine(dir, "winget.exe"))) return Path.Combine(dir, "winget.exe");
        }
        catch { /* accès refusé à WindowsApps : normal hors élévation */ }
        return alias;
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
/// dans une chaîne interprétée. C'est la règle anti-injection n°1 de PC Pilot.
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

    /// <summary>Lance un outil graphique (panneau de configuration, MMC…) sans attendre, sans shell.</summary>
    public static void Launch(SystemTool tool, params string[] args)
    {
        var psi = new ProcessStartInfo(SystemTools.Resolve(tool)) { UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process.Start(psi)?.Dispose();
    }

    /// <summary>
    /// Ouvre une page des Paramètres Windows. Seul le schéma ms-settings: (et quelques schémas système) est accepté.
    /// </summary>
    public static void OpenSettingsUri(string uri)
    {
        if (!Regex.IsMatch(uri, @"^(ms-settings|windowsdefender|ms-windows-store|ms-availablenetworks|ms-actioncenter):[A-Za-z0-9\-_./?=&]*$"))
            throw new ArgumentException("URI non autorisée : " + uri);
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
            throw new ArgumentException("Adresse non autorisée : " + url);
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
    }

    /// <summary>Ouvre un dossier local existant dans l'Explorateur.</summary>
    public static void OpenFolder(string path)
    {
        var full = Path.GetFullPath(path);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException(full);
        Launch(SystemTool.Explorer, full);
    }
}

/// <summary>
/// Exécution de scripts PowerShell CONSTANTS. Les données variables ne sont jamais insérées dans le texte du
/// script : elles sont passées en variables d'environnement PCP_* (lues via $env:PCP_NOM), ce qui rend
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
                if (v.Length > 8192 || v.Contains('\0')) throw new ArgumentException("Valeur de paramètre refusée : " + k);
                env["PCP_" + k] = v;
            }
        }

        const string prelude = "$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';" +
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
