using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Timonier.Core.Engine;
using Timonier.Core.Platform;
using Timonier.UI.Services;

namespace Timonier.Modules.Maintenance;

/// <summary>Section « Réparation » : outils officiels, un seul à la fois, avec progression relayée depuis le broker.</summary>
internal sealed partial class RepairPanel
{
    public FrameworkElement Root { get; }

    private sealed record Tool(string Key, string Glyph, string Title, string Description, string Duration, bool Admin, Func<Task> Run);

    private readonly List<Button> _runButtons = [];
    private readonly Border _jobBar;
    private readonly TextBlock _jobTitle = MaintUi.Text("", "Pp.CardTitle");
    private readonly TextBlock _jobStatus = MaintUi.Text("", "Pp.Caption");
    private readonly ProgressBar _jobProgress = new() { Height = 4, Minimum = 0, Maximum = 100, IsIndeterminate = true, Margin = new Thickness(0, 10, 0, 8) };
    private readonly Border _resultBar;
    private readonly TextBlock _resultText, _resultIcon;
    private bool _busy;

    // Pourcentage en fin de ligne, placé après (« 45 % ») ou avant (« %45 ») le nombre selon la culture de l'interface.
    [GeneratedRegex(@"(?:(\d{1,3})\s?%|%\s?(\d{1,3}))\s*$")]
    private static partial Regex PercentRx();

    public RepairPanel()
    {
        var tools = new List<Tool>
        {
            new("sfc", "", L("Check system files (SFC)"),
                L("Looks for damaged or modified Windows files and replaces them with healthy copies (sfc /scannow). Run this first if something behaves abnormally."),
                L("10 to 30 min"), true, () => StartSfcAsync()),
            new("dism", "", L("Repair Windows image (DISM)"),
                L("Repairs the component store that SFC relies on, by downloading healthy items from Windows Update (internet connection required). Run it if SFC couldn't repair everything."),
                L("10 to 40 min"), true, () => RunActionJobAsync(RepairDismAction.RestoreHealthId, L("Windows image repair"),
                    L("DISM will check and repair the Windows image by downloading the necessary components from Windows Update. This can take up to 40 minutes."))),
            new("chkdsk", "", L("Scan system disk"),
                L("Checks the system drive's file system online (chkdsk /scan), without locking up the PC or restarting. Windows fixes what it can on the fly and reports the rest."),
                L("2 to 20 min"), true, () => RunActionJobAsync(ChkdskScanAction.ActionId, L("System disk scan"), null)),
            new("componentcleanup", "", L("Clean up the component store"),
                L("Immediately deletes old versions of components replaced by updates (WinSxS folder) and often frees up several hundred MB. Updates already installed can no longer be uninstalled."),
                L("5 to 30 min"), true, () => RunActionJobAsync(RepairDismAction.ComponentCleanupId, L("Component store cleanup"),
                    L("Old versions of components will be deleted immediately: you won't be able to uninstall updates that are already installed. Continue?"))),
            new("iconcache", "", L("Reset the icon and thumbnail cache"),
                L("Fixes blank or wrong icons and incorrect previews. Windows Explorer is closed for a few seconds (the taskbar and desktop disappear), the caches are deleted, then everything is rebuilt."),
                L("a few seconds"), false, ResetIconCacheAsync),
            new("wsreset", "", L("Reset Microsoft Store cache"),
                L("Clears the Microsoft Store cache (WSReset) when the Store won't open or no longer downloads. The Store opens when it's done; your apps and their data aren't touched."),
                L("less than a minute"), false, ResetStoreAsync),
            new("time", "", L("Resync time"),
                L("Forces the clock to sync immediately with the Windows time server (internet connection required). Useful if the time drifts or after a battery replacement."),
                L("a few seconds"), true, () => RunActionJobAsync(TimeResyncAction.ActionId, L("Time sync"), null)),
        };

        var root = new StackPanel();
        _jobProgress.SetResourceReference(Control.ForegroundProperty, "Pp.Accent");
        var jobBody = new StackPanel();
        var jobHead = new DockPanel();
        var spinnerIcon = MaintUi.Icon("", 16, "Pp.AccentText");
        spinnerIcon.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(spinnerIcon, Dock.Left);
        jobHead.Children.Add(spinnerIcon);
        jobHead.Children.Add(_jobTitle);
        jobBody.Children.Add(jobHead);
        jobBody.Children.Add(_jobProgress);
        jobBody.Children.Add(_jobStatus);
        var hint = MaintUi.Text(L("You can keep using the PC and switch pages; keep Timonier open until it's done."), "Pp.Caption");
        hint.Margin = new Thickness(0, 4, 0, 0);
        jobBody.Children.Add(hint);
        _jobBar = new Border { Child = jobBody, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 10) }.Styled("Pp.InfoBar");
        root.Children.Add(_jobBar);

        (_resultBar, _resultText, _resultIcon) = MaintUi.InfoBar();
        _resultBar.Margin = new Thickness(0, 0, 0, 10);
        root.Children.Add(_resultBar);

        var card = new StackPanel();
        foreach (var tool in tools)
        {
            if (card.Children.Count > 0) card.Children.Add(MaintUi.Divider(new Thickness(0, 12, 0, 12)));
            card.Children.Add(BuildRow(tool));
        }
        root.Children.Add(MaintUi.Card(card));
        Root = root;
    }

    private FrameworkElement BuildRow(Tool tool)
    {
        var texts = new StackPanel { Margin = new Thickness(12, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new WrapPanel();
        var title = MaintUi.Text(tool.Title, "Pp.CardTitle", wrap: false);
        title.Margin = new Thickness(0, 0, 8, 0);
        titleRow.Children.Add(title);
        titleRow.Children.Add(MaintUi.Badge(tool.Duration, "Pp.TextSecondary", "Pp.CardSecondary", ""));
        if (tool.Admin)
        {
            var b = MaintUi.Badge(L("Administrator"), "Pp.TextSecondary", "Pp.CardSecondary", "");
            b.Margin = new Thickness(6, 0, 0, 0);
            titleRow.Children.Add(b);
        }
        texts.Children.Add(titleRow);
        var desc = MaintUi.Text(tool.Description, "Pp.Caption");
        desc.Margin = new Thickness(0, 4, 0, 0);
        texts.Children.Add(desc);

        var button = MaintUi.Button(LC("repair tool", "Run"), "", "Pp.Button", async (_, _) => await tool.Run());
        button.VerticalAlignment = VerticalAlignment.Center;
        button.MinWidth = 96;
        System.Windows.Automation.AutomationProperties.SetName(button, L("Run: {0}", tool.Title));
        _runButtons.Add(button);

        var dock = new DockPanel();
        var icon = MaintUi.IconTile(tool.Glyph);
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(button, Dock.Right);
        dock.Children.Add(icon);
        dock.Children.Add(button);
        dock.Children.Add(texts);
        return dock;
    }

    private void SetBusy(bool busy, string title = "")
    {
        _busy = busy;
        foreach (var b in _runButtons) b.IsEnabled = !busy;
        _jobTitle.Text = title;
        _jobStatus.Text = busy ? L("Preparing…") : "";
        _jobProgress.IsIndeterminate = true;
        _jobProgress.Value = 0;
        _jobBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (busy) _resultBar.Visibility = Visibility.Collapsed;
    }

    private void OnProgress(string text)
    {
        _jobStatus.Text = text;
        var m = PercentRx().Match(text);
        if (m.Success && int.TryParse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value, out var pct) && pct is >= 0 and <= 100)
        {
            _jobProgress.IsIndeterminate = false;
            _jobProgress.Value = pct;
        }
    }

    private void ShowResult(ApplyOutcome outcome)
    {
        if (outcome.Cancelled)
        {
            MaintUi.SetInfo(_resultBar, _resultText, _resultIcon, L("Operation canceled: {0}", outcome.Message), "Pp.InfoBar", "");
            return;
        }
        MaintUi.SetInfo(_resultBar, _resultText, _resultIcon, outcome.Message,
            outcome.Success ? "Pp.InfoBar.Success" : "Pp.InfoBar.Danger", outcome.Success ? "" : "");
    }

    public Task StartSfcAsync() => RunActionJobAsync(RepairSfcAction.ActionId, L("System file check"),
        L("SFC will scan all protected Windows files and replace the damaged ones. It takes 10 to 30 minutes; administrator permission will be requested."));

    private async Task RunActionJobAsync(string actionId, string title, string? confirm)
    {
        if (_busy)
        {
            AppHost.Toasts.Show(L("A repair operation is already in progress."), ToastKind.Info);
            return;
        }
        if (confirm is not null && !await AppHost.Dialogs.ConfirmAsync(title, confirm, LC("repair tool", "Run"), L("Undo"))) return;

        SetBusy(true, title);
        using var keepAlive = AppHost.Background.Acquire(title);
        try
        {
            var outcome = await AppHost.Engine.RunActionAsync(actionId, null, new Progress<string>(OnProgress));
            ShowResult(outcome);
            AppHost.Toasts.ShowOutcome(outcome);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ResetIconCacheAsync()
    {
        if (_busy) return;
        if (!await AppHost.Dialogs.ConfirmAsync(L("Reset icon cache"),
                L("Windows Explorer will be closed for a few seconds: the taskbar, desktop and folder windows will disappear and then come back. Open File Explorer windows will be closed."), L("Reset"), L("Undo"))) return;

        SetBusy(true, L("Resetting the icon cache"));
        try
        {
            var (deleted, locked) = await Task.Run(async () =>
            {
                await ProcessRunner.RunAsync(SystemTool.TaskKill, ["/f", "/im", "explorer.exe"], new RunOptions { Timeout = TimeSpan.FromSeconds(15) });
                try
                {
                    await Task.Delay(1200);
                    return DeleteIconCaches();
                }
                finally
                {
                    try { ProcessRunner.Launch(SystemTool.Explorer); }
                    catch (Exception ex) { Log.Error("Maintenance", "relance de l'Explorateur", ex); }
                }
            });
            var outcome = new ApplyOutcome(true, locked == 0
                ? LP(deleted, "Icon cache reset ({0} file deleted). Icons are rebuilt as they're displayed.",
                    "Icon cache reset ({0} files deleted). Icons are rebuilt as they're displayed.")
                : L("Icon cache reset ({0}, {1}). If some icons are still wrong, restart the PC.",
                    LP(deleted, "{0} file deleted", "{0} files deleted"), LP(locked, "{0} still in use", "{0} still in use")));
            ShowResult(outcome);
            AppHost.Toasts.ShowOutcome(outcome);
        }
        catch (Exception ex)
        {
            Log.Error("Maintenance", "cache des icônes", ex);
            ShowResult(new ApplyOutcome(false, L("The reset failed: {0}", ex.Message)));
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Supprime iconcache_*.db, thumbcache_*.db et l'ancien IconCache.db (suppression sûre, sans suivre de lien).</summary>
    private static (int Deleted, int Locked) DeleteIconCaches()
    {
        int deleted = 0, locked = 0;
        void Delete(string root, IEnumerable<string> files)
        {
            var rootFinal = SafeDelete.RootFinalPath(root);
            if (rootFinal is null) return;
            foreach (var f in files)
            {
                switch (SafeDelete.Delete(f, rootFinal, directory: false))
                {
                    case SafeDelete.Outcome.Deleted: deleted++; break;
                    case SafeDelete.Outcome.Missing: break;
                    default: locked++; break;
                }
            }
        }
        try
        {
            var explorer = CleanupCatalog.ExplorerCacheDir;
            if (Directory.Exists(explorer))
                Delete(explorer, Directory.EnumerateFiles(explorer, "iconcache_*.db").Concat(Directory.EnumerateFiles(explorer, "thumbcache_*.db")).ToList());
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var legacy = Path.Combine(local, "IconCache.db");
            if (File.Exists(legacy)) Delete(local, [legacy]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Maintenance", "cache des icônes : " + ex.Message);
        }
        return (deleted, locked);
    }

    private async Task ResetStoreAsync()
    {
        if (_busy) return;
        SetBusy(true, L("Resetting the Microsoft Store cache"));
        try
        {
            _jobStatus.Text = L("WSReset clears the cache; the Microsoft Store will open when it's done.");
            var r = await ProcessRunner.RunAsync(SystemTool.WsReset, [], new RunOptions { Timeout = TimeSpan.FromMinutes(3) });
            var outcome = r.TimedOut
                ? new ApplyOutcome(false, L("WSReset didn't respond within 3 minutes."))
                : new ApplyOutcome(true, L("Microsoft Store cache reset: the Store should open."));
            ShowResult(outcome);
            AppHost.Toasts.ShowOutcome(outcome);
        }
        catch (Exception ex)
        {
            Log.Error("Maintenance", "wsreset", ex);
            ShowResult(new ApplyOutcome(false, L("Couldn't start WSReset: {0}", ex.Message)));
        }
        finally
        {
            SetBusy(false);
        }
    }
}
