using System.Windows;
using System.Windows.Controls;
using Timonier.Core.Engine;
using Timonier.Core.Model;
using Timonier.Core.Platform;
using Timonier.Core.Settings;
using Timonier.UI.Services;
using static Timonier.Modules.Profiles.ProfilesUi;

namespace Timonier.Modules.Profiles;

/// <summary>Étapes 4 et 5 : exécution séquentielle (annulable entre deux éléments) et rapport.</summary>
public sealed partial class ProfilesPage
{
    private enum ItemState { Pending, Running, Done, Failed, Skipped }

    private sealed class RunItem(string title, string? detail = null)
    {
        public string Title { get; } = title;
        public string? Detail { get; set; } = detail;
        public ItemState State { get; set; } = ItemState.Pending;
        public string? Message { get; set; }
        public TweakDefinition? Tweak { get; init; }
        public string? Option { get; init; }
        public PlanApp? App { get; init; }
        /// <summary>Libellé du rapport quand l'élément a réussi (par défaut « Appliqué »).</summary>
        public string DoneLabel { get; set; } = L("Applied");
    }

    private sealed class RunState
    {
        public RunItem? RestorePoint { get; set; }
        public List<RunItem> Tweaks { get; } = [];
        public List<RunItem> Apps { get; } = [];
        public ApplyEffect Effects { get; set; }
        public bool Cancelled { get; set; }
        public bool Aborted { get; set; }
        public List<string> Log { get; } = [];
        public IEnumerable<RunItem> All => RestorePoint is { } rp ? Tweaks.Prepend(rp).Concat(Apps) : Tweaks.Concat(Apps);
    }

    private bool _running;
    private RunState? _run;
    private CancellationTokenSource? _applyCts;
    private ProgressBar? _progressBar;
    private TextBlock? _progressText;
    private StackPanel? _phaseList;
    private TextBlock? _logText;
    private Button? _cancelButton;

    // ------------------------------------------------------------------ Confirmation

    private async Task StartApplyAsync()
    {
        if (_running || _plan is not { } plan) return;
        var tweaks = plan.SelectedTweaks.ToList();
        var apps = plan.SelectedApps.ToList();
        if (tweaks.Count + apps.Count == 0) return;

        var restore = _createRestorePoint && AppHost.Registry.GetAction(RestorePointAction) is not null;
        var content = new StackPanel { MaxWidth = 520 };
        content.Children.Add(Text(L("Timonier will now, in this order:")));
        var steps = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
        if (restore) steps.Children.Add(Bullet(L("create a system restore point;"), "", "Pp.AccentText"));
        if (tweaks.Count > 0)
        {
            var admin = tweaks.Count(t => t.Tweak.RequiresAdmin);
            steps.Children.Add(Bullet(admin > 0
                ? LP(tweaks.Count, "apply {0} setting, {1} with administrator rights;", "apply {0} settings, {1} of them with administrator rights;", admin)
                : LP(tweaks.Count, "apply {0} setting;", "apply {0} settings;"), "", "Pp.AccentText"));
        }
        if (apps.Count > 0)
            steps.Children.Add(Bullet(LP(apps.Count, "install {0} app with winget (downloaded from the internet, accepting the publishers' licenses): {1}{2}.",
                    "install {0} apps with winget (downloaded from the internet, accepting the publishers' licenses): {1}{2}.",
                    string.Join(", ", apps.Take(6).Select(a => a.Name)), apps.Count > 6 ? "…" : ""),
                "", "Pp.AccentText"));
        content.Children.Add(steps);

        var needsAdmin = tweaks.Any(t => t.Tweak.RequiresAdmin) || apps.Count > 0 || restore;
        if (needsAdmin)
            content.Children.Add(Caption(L("Windows will ask for administrator permission (UAC) at most once for the whole plan.")
                + (apps.Any(a => !a.InCatalog) ? " " + L("Apps outside the verified catalog also require a separate confirmation.") : "")));
        var warnings = tweaks.Where(t => t.Tweak.Warning is not null).ToList();
        if (warnings.Count > 0)
        {
            var items = string.Join(" ", warnings.Take(4).Select(t => L("“{0}”: {1}", t.Tweak.Title, t.Tweak.Warning)));
            if (warnings.Count > 4)
                items += " " + LP(warnings.Count - 4, "(and {0} more warning in the plan)", "(and {0} more warnings in the plan)");
            var w = Caption(L("Good to know: {0}", items),
                "Pp.Warning");
            w.Margin = new Thickness(0, 8, 0, 0);
            content.Children.Add(w);
        }
        if (EffectsText(tweaks.Aggregate(ApplyEffect.None, (e, t) => e | t.Tweak.Effect)) is { } fx)
        {
            var f = Caption(L("Some changes will require: {0}. Nothing restarts without your consent.", fx));
            f.Margin = new Thickness(0, 8, 0, 0);
            content.Children.Add(f);
        }
        var undo = Caption(tweaks.Any(t => !t.Tweak.IsReversible)
            ? L("You can undo each setting from History, except those marked “Can't be undone”. Apps can be uninstalled from the Apps page or Windows Settings.")
            : L("You can undo each setting from History. Apps can be uninstalled from the Apps page or Windows Settings."));
        undo.Margin = new Thickness(0, 8, 0, 0);
        content.Children.Add(undo);

        if (!await AppHost.Dialogs.ShowAsync(L("Apply the plan?"), content, L("Apply"), L("Undo"))) return;
        await RunAsync(tweaks, apps, restore);
    }

    // ------------------------------------------------------------------ Exécution

    private async Task RunAsync(List<PlanTweak> tweaks, List<PlanApp> apps, bool restore)
    {
        var run = _run = new RunState();
        if (restore) run.RestorePoint = new RunItem(L("System restore point"));
        foreach (var t in tweaks) run.Tweaks.Add(new RunItem(t.Tweak.Title, t.TargetLabel) { Tweak = t.Tweak, Option = t.Target });
        foreach (var a in apps) run.Apps.Add(new RunItem(a.Name) { App = a });

        _running = true;
        var cts = _applyCts = new CancellationTokenSource();
        using var keepAlive = AppHost.Background.Acquire(L("Applying a setup profile"));
        GoTo(Step.Apply);
        var progress = new Progress<string>(message => AddLog(message));

        try
        {
            // 1. Point de restauration
            if (run.RestorePoint is { } rp && !cts.IsCancellationRequested)
            {
                SetState(rp, ItemState.Running);
                var outcome = await AppHost.Engine.RunActionAsync(RestorePointAction,
                    new Dictionary<string, string> { ["description"] = L("Timonier - before profile") }, progress, cts.Token);
                rp.Message = outcome.Message;
                // Windows ne crée qu'un point toutes les 24 heures : l'action réussit alors sans nouveau point.
                rp.DoneLabel = outcome.Data?.GetValueOrDefault("created") == "false" ? L("Recent restore point kept") : L("Created");
                SetState(rp,outcome.Success ? ItemState.Done : outcome.Cancelled ? ItemState.Skipped : ItemState.Failed);
                if (!outcome.Success && !cts.IsCancellationRequested &&
                    !await AppHost.Dialogs.ConfirmAsync(L("Restore point not created"),
                        L("{0}\n\nContinue without a restore point? Settings can still be undone one by one from History.", outcome.Message),
                        L("Continue"), L("Stop")))
                {
                    run.Aborted = true;
                }
            }

            // 2. Réglages : une seule liste pour ApplyManyAsync (une seule session administrateur). L'annulation
            //    est honorée entre deux réglages : on arrête simplement d'en fournir.
            if (!run.Aborted && run.Tweaks.Count > 0 && !cts.IsCancellationRequested)
            {
                var queue = new Queue<RunItem>(run.Tweaks);
                IEnumerable<(TweakDefinition, string)> Items()
                {
                    while (queue.Count > 0 && !cts.IsCancellationRequested)
                    {
                        var next = queue.Dequeue();
                        Dispatcher.Invoke(() => SetState(next, ItemState.Running));
                        yield return (next.Tweak!, next.Option!);
                    }
                }
                var results = await Task.Run(() => AppHost.Engine.ApplyManyAsync(Items(), progress, CancellationToken.None));
                foreach (var (tweak, outcome) in results)
                {
                    var item = run.Tweaks.First(i => ReferenceEquals(i.Tweak, tweak));
                    item.Message = outcome.Message;
                    SetState(item, outcome.Success ? ItemState.Done : outcome.Cancelled ? ItemState.Skipped : ItemState.Failed);
                    if (outcome.Success) run.Effects |= outcome.Effect;
                }
                if (results.Count > 0 && results[^1].Outcome.Cancelled) run.Cancelled = true;
            }

            // 3. Applications, par lots de 40 au plus (contrat de apps.winget.install).
            if (!run.Aborted && run.Apps.Count > 0 && !cts.IsCancellationRequested)
            {
                if (AppHost.Registry.GetAction(WingetInstallAction) is null)
                {
                    foreach (var item in run.Apps) { item.Message = L("Apps module unavailable."); SetState(item, ItemState.Failed); }
                }
                else
                {
                    foreach (var chunk in run.Apps.Chunk(40))
                    {
                        if (cts.IsCancellationRequested) break;
                        foreach (var item in chunk) SetState(item, ItemState.Running);
                        var ids = string.Join(",", chunk.Select(i => i.App!.Id));
                        var outcome = await AppHost.Engine.RunActionAsync(WingetInstallAction, new Dictionary<string, string> { ["ids"] = ids }, progress, cts.Token);
                        foreach (var item in chunk)
                        {
                            if (outcome.Data?.TryGetValue(item.App!.Id, out var result) == true)
                            {
                                // Données de apps.winget.install : « ok : … » / « erreur : … » (préfixes non traduits), le détail
                                // d'une annulation étant L("annulé") dans les deux processus (même langue).
                                var success = result.StartsWith("ok", StringComparison.OrdinalIgnoreCase);
                                item.Message = result.Contains(':') ? result[(result.IndexOf(':') + 1)..].Trim() : result;
                                SetState(item, success ? ItemState.Done : item.Message == L("canceled") ? ItemState.Skipped : ItemState.Failed);
                            }
                            else
                            {
                                item.Message = outcome.Message;
                                SetState(item, outcome.Cancelled ? ItemState.Skipped : outcome.Success ? ItemState.Done : ItemState.Failed);
                            }
                        }
                        if (outcome.Success) run.Effects |= outcome.Effect;
                        if (outcome.Cancelled) break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Profiles", "application du plan", ex);
            AddLog(L("Unexpected error: {0}", ex.Message));
            run.Aborted = true;
        }
        finally
        {
            if (cts.IsCancellationRequested) run.Cancelled = true;
            foreach (var item in run.All.Where(i => i.State is ItemState.Pending or ItemState.Running))
            {
                item.State = ItemState.Skipped;
                item.Message ??= run.Aborted ? L("Not done (stop requested).") : L("Not done (canceled).");
            }
            _running = false;
            _applyCts = null;
            cts.Dispose();
        }

        if (run.Effects.HasFlag(ApplyEffect.Reboot)) SystemEffects.MarkReboot();
        if (run.Effects.HasFlag(ApplyEffect.SignOut)) SystemEffects.MarkSignOut();
        SettingsStore.Update(s => s.FirstRunCompleted = true);

        var ok = run.All.Count(i => i.State == ItemState.Done);
        var failed = run.All.Count(i => i.State == ItemState.Failed);
        AppHost.Toasts.Show(failed == 0 && !run.Cancelled && !run.Aborted
                ? LP(ok, "Plan applied: {0} item completed.", "Plan applied: {0} items completed.")
                : run.Cancelled || run.Aborted
                    ? LP(failed, "Plan finished with {0} failure, interrupted: see the report.", "Plan finished with {0} failures, interrupted: see the report.")
                    : LP(failed, "Plan finished with {0} failure: see the report.", "Plan finished with {0} failures: see the report."),
            failed == 0 && !run.Cancelled && !run.Aborted ? ToastKind.Success : ToastKind.Warning);
        GoTo(Step.Report);
    }

    private void SetState(RunItem item, ItemState state)
    {
        item.State = state;
        if (_step == Step.Apply) RefreshApply();
    }

    private void AddLog(string message)
    {
        if (_run is null || string.IsNullOrWhiteSpace(message)) return;
        var line = message.Trim();
        if (line.Length > 160) line = line[..157] + "…";
        _run.Log.Add(line);
        if (_run.Log.Count > 200) _run.Log.RemoveAt(0);
        if (_logText is not null) _logText.Text = string.Join("\n", _run.Log.TakeLast(6));
        if (_progressText is not null) _progressText.Text = line;
    }

    private void RenderApply()
    {
        if (_run is not { } run) return;
        var top = new StackPanel();
        top.Children.Add(Text(L("Applying"), "Pp.CardTitle"));
        _progressText = Caption(L("Preparing…"));
        _progressText.Margin = new Thickness(0, 4, 0, 10);
        _progressText.TextTrimming = TextTrimming.CharacterEllipsis;
        _progressText.TextWrapping = TextWrapping.NoWrap;
        top.Children.Add(_progressText);
        _progressBar = new ProgressBar { Height = 4, Minimum = 0, Maximum = Math.Max(1, run.All.Count()) };
        top.Children.Add(_progressBar);
        var hint = Caption(L("You can leave this page: the operation continues. If Windows asks for administrator permission, accept it to continue."), "Pp.TextTertiary");
        hint.Margin = new Thickness(0, 10, 0, 0);
        top.Children.Add(hint);
        _body.Children.Add(Card(top));

        _phaseList = new StackPanel();
        _body.Children.Add(Card(_phaseList));

        _body.Children.Add(Section(L("Details")));
        _logText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"), FontSize = 12 }
            .Themed(TextBlock.ForegroundProperty, "Pp.TextSecondary");
        _logText.Text = run.Log.Count == 0 ? "…" : string.Join("\n", run.Log.TakeLast(6));
        _body.Children.Add(Card(_logText));

        _cancelButton = Button(L("Cancel the rest"), "", "Pp.Button", (_, _) =>
        {
            _applyCts?.Cancel();
            if (_cancelButton is null) return;
            _cancelButton.IsEnabled = false;
            _cancelButton.Content = L("Stopping after the current item…");
        });
        _cancelButton.ToolTip = L("The current item is finishing; the following ones won't be applied.");
        _cancelButton.IsEnabled = _applyCts is { IsCancellationRequested: false };
        NavRight(_cancelButton);
        RefreshApply();
    }

    private void RefreshApply()
    {
        if (_run is not { } run || _phaseList is null) return;
        var all = run.All.ToList();
        if (_progressBar is not null) _progressBar.Value = all.Count(i => i.State is ItemState.Done or ItemState.Failed or ItemState.Skipped);

        _phaseList.Children.Clear();
        if (run.RestorePoint is { } rp) _phaseList.Children.Add(PhaseRow(L("Restore point"), [rp]));
        if (run.Tweaks.Count > 0) _phaseList.Children.Add(PhaseRow(L("Settings"), run.Tweaks));
        if (run.Apps.Count > 0) _phaseList.Children.Add(PhaseRow(L("Apps"), run.Apps));
    }

    private static UIElement PhaseRow(string title, IReadOnlyList<RunItem> items)
    {
        var done = items.Count(i => i.State == ItemState.Done);
        var failed = items.Count(i => i.State == ItemState.Failed);
        var running = items.FirstOrDefault(i => i.State == ItemState.Running);
        var finished = items.All(i => i.State is ItemState.Done or ItemState.Failed or ItemState.Skipped);
        var (glyph, brush) = running is not null ? ("", "Pp.AccentText")
            : finished ? (failed > 0 ? ("", "Pp.Warning") : items.All(i => i.State == ItemState.Skipped) ? ("", "Pp.TextTertiary") : ("", "Pp.Success"))
            : ("", "Pp.TextTertiary");
        string detail;
        if (items.Count == 1 && running is null && items[0].Message is { } m) detail = m;
        else
        {
            detail = LP(done, "{0} of {1} done", "{0} of {1} done", items.Count);
            if (failed > 0) detail += ", " + LP(failed, "{0} failed", "{0} failed");
            if (running is not null) detail += " · " + L("in progress: {0}", running.Title);
        }
        return SimpleRow(glyph, brush, title, detail);
    }

    // ------------------------------------------------------------------ Rapport

    private void RenderReport()
    {
        if (_run is not { } run) return;
        var all = run.All.ToList();
        var done = all.Count(i => i.State == ItemState.Done);
        var failed = all.Count(i => i.State == ItemState.Failed);
        var skipped = all.Count(i => i.State == ItemState.Skipped);

        var parts = new List<string> { LP(done, "{0} item applied", "{0} items applied") };
        if (failed > 0) parts.Add(LP(failed, "{0} failed", "{0} failed"));
        if (skipped > 0) parts.Add(LP(skipped, "{0} not done", "{0} not done"));
        var summary = string.Join(", ", parts) + ".";
        if (run.Cancelled) summary += " " + L("You interrupted the operation: what was applied before stopping stays in place.");
        else if (run.Aborted) summary += " " + L("The operation was stopped before the end.");
        _body.Children.Add(InfoBar(summary, failed == 0 && skipped == 0 ? "" : "",
            failed == 0 && skipped == 0 ? "Pp.InfoBar.Success" : "Pp.InfoBar.Warning"));

        if (run.Effects.HasFlag(ApplyEffect.Reboot))
            _body.Children.Add(InfoBar(L("Some changes will take effect after the PC restarts."), "", "Pp.InfoBar",
                Button(L("Restart now"), "", "Pp.Button", async (_, _) =>
                {
                    if (await AppHost.Dialogs.ConfirmAsync(L("Restart the PC?"), L("Save your work: open apps will be closed."), L("Restart")))
                        await SystemEffects.RebootNowAsync();
                })));
        else if (run.Effects.HasFlag(ApplyEffect.SignOut))
            _body.Children.Add(InfoBar(L("Some changes will take effect the next time you sign in."), "", "Pp.InfoBar",
                Button(L("Sign out"), null, "Pp.Button", async (_, _) =>
                {
                    if (await AppHost.Dialogs.ConfirmAsync(L("Sign out?"), L("Save your work before continuing."), L("Sign out")))
                        await SystemEffects.SignOutNowAsync();
                })));
        if (run.Effects.HasFlag(ApplyEffect.RestartExplorer))
            _body.Children.Add(InfoBar(L("Windows Explorer needs to be restarted to show some changes (taskbar, menus)."), "", "Pp.InfoBar",
                Button(L("Restart Explorer"), null, "Pp.Button", (_, _) => _ = SystemEffects.RestartExplorerAsync())));

        if (_plan?.Profiles.FirstOrDefault(p => p.LinkPageId == "kiosk") is { } kiosk && AppHost.Registry.GetPage("kiosk") is not null)
            _body.Children.Add(InfoBar(L("The PC is ready to be used as a kiosk. You still need to choose the account and the displayed app."), "", "Pp.InfoBar",
                Button(kiosk.LinkLabel ?? L("Set up the kiosk"), null, "Pp.AccentButton", (_, _) => AppHost.Navigator.Navigate("kiosk"))));

        if (run.RestorePoint is { } rp)
        {
            _body.Children.Add(Section(L("Restore point")));
            _body.Children.Add(Card(ReportRow(rp), 12));
        }
        if (run.Tweaks.Count > 0)
        {
            _body.Children.Add(Section(L("Settings ({0} of {1})", run.Tweaks.Count(i => i.State == ItemState.Done), run.Tweaks.Count)));
            _body.Children.Add(Card(ReportList(run.Tweaks.OrderBy(i => i.State == ItemState.Done ? 1 : 0))));
        }
        if (run.Apps.Count > 0)
        {
            _body.Children.Add(Section(L("Apps ({0} of {1})", run.Apps.Count(i => i.State == ItemState.Done), run.Apps.Count)));
            _body.Children.Add(Card(ReportList(run.Apps.OrderBy(i => i.State == ItemState.Done ? 1 : 0))));
        }

        if (AppHost.Registry.GetPage("journal") is null && run.Tweaks.Any(i => i.State == ItemState.Done))
        {
            var back = Caption(L("To revert a setting, open its page (Privacy, Performance, Security…) and choose the original option again."), "Pp.TextTertiary");
            back.Margin = new Thickness(0, 8, 0, 0);
            _body.Children.Add(back);
        }
        else if (AppHost.Registry.GetPage("journal") is not null)
            NavLeft(Button(L("Open History (undo)"), "", "Pp.Button", (_, _) => AppHost.Navigator.Navigate("journal")));
        NavLeft(Button(L("Export this plan…"), "", "Pp.SubtleButton", (_, _) => _ = ExportAsync()));
        NavRight(Button(L("Finish"), "", "Pp.AccentButton", (_, _) => Finish()));
    }

    private static StackPanel ReportList(IEnumerable<RunItem> items)
    {
        var list = new StackPanel();
        var first = true;
        foreach (var item in items)
        {
            if (!first) list.Children.Add(Divider(4, 4));
            first = false;
            list.Children.Add(ReportRow(item));
        }
        return list;
    }

    private static UIElement ReportRow(RunItem item)
    {
        var (glyph, brush, label) = item.State switch
        {
            ItemState.Done => ("", "Pp.Success", item.DoneLabel),
            ItemState.Failed => ("", "Pp.Danger", L("Failed")),
            _ => ("", "Pp.TextTertiary", L("Not done")),
        };
        var detail = item.Detail is null ? label : L("{0}: {1}", label, item.Detail);
        if (item.State != ItemState.Done && item.Message is { Length: > 0 } msg) detail += " — " + msg;
        else if (item.Tweak is null && item.Message is { Length: > 0 } ok) detail += " — " + ok; // application ou point de restauration
        var row = (DockPanel)SimpleRow(glyph, brush, item.Title, detail);
        var badges = new WrapPanel { Margin = new Thickness(22, 2, 0, 0) };
        if (item.State == ItemState.Done && item.Tweak is { } t)
        {
            if (!t.IsReversible) badges.Children.Add(Badge(L("Can't be undone"), "Danger"));
            if (t.Effect.HasFlag(ApplyEffect.Reboot)) badges.Children.Add(Badge(L("Restart required"), "Info", ""));
            else if (t.Effect.HasFlag(ApplyEffect.SignOut)) badges.Children.Add(Badge(L("Sign-out required"), "Info", ""));
        }
        if (badges.Children.Count == 0) return row;
        var stack = new StackPanel();
        stack.Children.Add(row);
        stack.Children.Add(badges);
        return stack;
    }

    private void Finish()
    {
        SettingsStore.Update(s => s.FirstRunCompleted = true);
        _run = null;
        _plan = null;
        _import = null;
        _defaultsApplied = _userTouched = false;
        GoTo(Step.Pc);
        AppHost.Toasts.Show(L("Setup complete. You can run the wizard again at any time."), ToastKind.Success);
    }
}
