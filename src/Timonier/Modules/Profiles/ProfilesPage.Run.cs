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
        public string DoneLabel { get; set; } = "Appliqué";
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
        content.Children.Add(Text("Timonier va maintenant, dans cet ordre :"));
        var steps = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
        if (restore) steps.Children.Add(Bullet("créer un point de restauration système ;", "", "Pp.AccentText"));
        if (tweaks.Count > 0)
        {
            var admin = tweaks.Count(t => t.Tweak.RequiresAdmin);
            steps.Children.Add(Bullet($"appliquer {tweaks.Count} réglage{(tweaks.Count > 1 ? "s" : "")}" +
                                      (admin > 0 ? $", dont {admin} avec les droits d'administrateur ;" : " ;"), "", "Pp.AccentText"));
        }
        if (apps.Count > 0)
            steps.Children.Add(Bullet($"installer {apps.Count} application{(apps.Count > 1 ? "s" : "")} avec winget (téléchargement depuis Internet, " +
                                      "acceptation des licences des éditeurs) : " + string.Join(", ", apps.Take(6).Select(a => a.Name)) + (apps.Count > 6 ? "…" : "") + ".",
                "", "Pp.AccentText"));
        content.Children.Add(steps);

        var needsAdmin = tweaks.Any(t => t.Tweak.RequiresAdmin) || apps.Count > 0 || restore;
        if (needsAdmin)
            content.Children.Add(Caption("Windows demandera au plus une fois l'autorisation administrateur (UAC) pour l'ensemble du plan." +
                                         (apps.Any(a => !a.InCatalog) ? " Les applications hors du catalogue vérifié demandent en plus une confirmation dédiée." : "")));
        var warnings = tweaks.Where(t => t.Tweak.Warning is not null).ToList();
        if (warnings.Count > 0)
        {
            var w = Caption("À savoir : " + string.Join(" ", warnings.Take(4).Select(t => $"« {t.Tweak.Title} » : {t.Tweak.Warning}")) +
                            (warnings.Count > 4 ? $" (et {warnings.Count - 4} autre{(warnings.Count - 4 > 1 ? "s" : "")} avertissement{(warnings.Count - 4 > 1 ? "s" : "")} dans le plan)" : ""),
                "Pp.Warning");
            w.Margin = new Thickness(0, 8, 0, 0);
            content.Children.Add(w);
        }
        if (EffectsText(tweaks.Aggregate(ApplyEffect.None, (e, t) => e | t.Tweak.Effect)) is { } fx)
        {
            var f = Caption("Certains changements nécessiteront : " + fx + ". Rien ne redémarre sans votre accord.");
            f.Margin = new Thickness(0, 8, 0, 0);
            content.Children.Add(f);
        }
        var undo = Caption("Vous pourrez annuler chaque réglage depuis le Journal" +
                           (tweaks.Any(t => !t.Tweak.IsReversible) ? ", sauf ceux marqués « Non annulable »." : ".") +
                           " Les applications se désinstallent depuis la page Applications ou les Paramètres de Windows.");
        undo.Margin = new Thickness(0, 8, 0, 0);
        content.Children.Add(undo);

        if (!await AppHost.Dialogs.ShowAsync("Appliquer le plan ?", content, "Appliquer", "Annuler")) return;
        await RunAsync(tweaks, apps, restore);
    }

    // ------------------------------------------------------------------ Exécution

    private async Task RunAsync(List<PlanTweak> tweaks, List<PlanApp> apps, bool restore)
    {
        var run = _run = new RunState();
        if (restore) run.RestorePoint = new RunItem("Point de restauration système");
        foreach (var t in tweaks) run.Tweaks.Add(new RunItem(t.Tweak.Title, t.TargetLabel) { Tweak = t.Tweak, Option = t.Target });
        foreach (var a in apps) run.Apps.Add(new RunItem(a.Name) { App = a });

        _running = true;
        var cts = _applyCts = new CancellationTokenSource();
        using var keepAlive = AppHost.Background.Acquire("Application d'un profil d'installation");
        GoTo(Step.Apply);
        var progress = new Progress<string>(message => AddLog(message));

        try
        {
            // 1. Point de restauration
            if (run.RestorePoint is { } rp && !cts.IsCancellationRequested)
            {
                SetState(rp, ItemState.Running);
                var outcome = await AppHost.Engine.RunActionAsync(RestorePointAction,
                    new Dictionary<string, string> { ["description"] = "Timonier - avant profil" }, progress, cts.Token);
                rp.Message = outcome.Message;
                // Windows ne crée qu'un point toutes les 24 heures : l'action réussit alors sans nouveau point.
                rp.DoneLabel = outcome.Data?.GetValueOrDefault("created") == "false" ? "Point récent conservé" : "Créé";
                SetState(rp,outcome.Success ? ItemState.Done : outcome.Cancelled ? ItemState.Skipped : ItemState.Failed);
                if (!outcome.Success && !cts.IsCancellationRequested &&
                    !await AppHost.Dialogs.ConfirmAsync("Point de restauration non créé",
                        $"{outcome.Message}\n\nContinuer sans point de restauration ? Les réglages resteront annulables un par un depuis le Journal.",
                        "Continuer", "Arrêter"))
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
                    foreach (var item in run.Apps) { item.Message = "Module Applications indisponible."; SetState(item, ItemState.Failed); }
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
                                var success = result.StartsWith("ok", StringComparison.OrdinalIgnoreCase);
                                item.Message = result.Contains(':') ? result[(result.IndexOf(':') + 1)..].Trim() : result;
                                SetState(item, success ? ItemState.Done : result.Contains("annul", StringComparison.OrdinalIgnoreCase) ? ItemState.Skipped : ItemState.Failed);
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
            AddLog("Erreur inattendue : " + ex.Message);
            run.Aborted = true;
        }
        finally
        {
            if (cts.IsCancellationRequested) run.Cancelled = true;
            foreach (var item in run.All.Where(i => i.State is ItemState.Pending or ItemState.Running))
            {
                item.State = ItemState.Skipped;
                item.Message ??= run.Aborted ? "Non effectué (arrêt demandé)." : "Non effectué (annulé).";
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
                ? $"Plan appliqué : {ok} élément{(ok > 1 ? "s" : "")} terminé{(ok > 1 ? "s" : "")}."
                : $"Plan terminé avec {failed} échec{(failed > 1 ? "s" : "")}{(run.Cancelled || run.Aborted ? ", interrompu" : "")} : consultez le rapport.",
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
        top.Children.Add(Text("Application en cours", "Pp.CardTitle"));
        _progressText = Caption("Préparation…");
        _progressText.Margin = new Thickness(0, 4, 0, 10);
        _progressText.TextTrimming = TextTrimming.CharacterEllipsis;
        _progressText.TextWrapping = TextWrapping.NoWrap;
        top.Children.Add(_progressText);
        _progressBar = new ProgressBar { Height = 4, Minimum = 0, Maximum = Math.Max(1, run.All.Count()) };
        top.Children.Add(_progressBar);
        var hint = Caption("Vous pouvez quitter cette page : l'opération continue. Si Windows demande l'autorisation administrateur, acceptez-la pour poursuivre.", "Pp.TextTertiary");
        hint.Margin = new Thickness(0, 10, 0, 0);
        top.Children.Add(hint);
        _body.Children.Add(Card(top));

        _phaseList = new StackPanel();
        _body.Children.Add(Card(_phaseList));

        _body.Children.Add(Section("Détails"));
        _logText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"), FontSize = 12 }
            .Themed(TextBlock.ForegroundProperty, "Pp.TextSecondary");
        _logText.Text = run.Log.Count == 0 ? "…" : string.Join("\n", run.Log.TakeLast(6));
        _body.Children.Add(Card(_logText));

        _cancelButton = Button("Annuler la suite", "", "Pp.Button", (_, _) =>
        {
            _applyCts?.Cancel();
            if (_cancelButton is null) return;
            _cancelButton.IsEnabled = false;
            _cancelButton.Content = "Arrêt après l'élément en cours…";
        });
        _cancelButton.ToolTip = "L'élément en cours se termine ; les suivants ne sont pas appliqués.";
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
        if (run.RestorePoint is { } rp) _phaseList.Children.Add(PhaseRow("Point de restauration", [rp]));
        if (run.Tweaks.Count > 0) _phaseList.Children.Add(PhaseRow("Réglages", run.Tweaks));
        if (run.Apps.Count > 0) _phaseList.Children.Add(PhaseRow("Applications", run.Apps));
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
        var detail = items.Count == 1 && running is null && items[0].Message is { } m ? m
            : $"{done} sur {items.Count} terminé{(done > 1 ? "s" : "")}" + (failed > 0 ? $", {failed} échec{(failed > 1 ? "s" : "")}" : "")
              + (running is not null ? $" · en cours : {running.Title}" : "");
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

        var summary = $"{done} élément{(done > 1 ? "s" : "")} appliqué{(done > 1 ? "s" : "")}"
                      + (failed > 0 ? $", {failed} échec{(failed > 1 ? "s" : "")}" : "")
                      + (skipped > 0 ? $", {skipped} non effectué{(skipped > 1 ? "s" : "")}" : "") + "."
                      + (run.Cancelled ? " Vous avez interrompu l'opération : ce qui a été appliqué avant l'arrêt reste en place." : "")
                      + (run.Aborted && !run.Cancelled ? " L'opération a été arrêtée avant la fin." : "");
        _body.Children.Add(InfoBar(summary, failed == 0 && skipped == 0 ? "" : "",
            failed == 0 && skipped == 0 ? "Pp.InfoBar.Success" : "Pp.InfoBar.Warning"));

        if (run.Effects.HasFlag(ApplyEffect.Reboot))
            _body.Children.Add(InfoBar("Certains changements seront effectifs après le redémarrage du PC.", "", "Pp.InfoBar",
                Button("Redémarrer maintenant", "", "Pp.Button", async (_, _) =>
                {
                    if (await AppHost.Dialogs.ConfirmAsync("Redémarrer le PC ?", "Enregistrez votre travail : les applications ouvertes seront fermées.", "Redémarrer"))
                        await SystemEffects.RebootNowAsync();
                })));
        else if (run.Effects.HasFlag(ApplyEffect.SignOut))
            _body.Children.Add(InfoBar("Certains changements seront effectifs à la prochaine ouverture de session.", "", "Pp.InfoBar",
                Button("Se déconnecter", null, "Pp.Button", async (_, _) =>
                {
                    if (await AppHost.Dialogs.ConfirmAsync("Se déconnecter ?", "Enregistrez votre travail avant de continuer.", "Se déconnecter"))
                        await SystemEffects.SignOutNowAsync();
                })));
        if (run.Effects.HasFlag(ApplyEffect.RestartExplorer))
            _body.Children.Add(InfoBar("L'Explorateur Windows doit être relancé pour afficher certains changements (barre des tâches, menus).", "", "Pp.InfoBar",
                Button("Relancer l'Explorateur", null, "Pp.Button", (_, _) => _ = SystemEffects.RestartExplorerAsync())));

        if (_plan?.Profiles.FirstOrDefault(p => p.LinkPageId == "kiosk") is { } kiosk && AppHost.Registry.GetPage("kiosk") is not null)
            _body.Children.Add(InfoBar("Le PC est préparé pour un usage en borne. Il reste à choisir le compte et l'application affichée.", "", "Pp.InfoBar",
                Button(kiosk.LinkLabel ?? "Configurer le kiosque", null, "Pp.AccentButton", (_, _) => AppHost.Navigator.Navigate("kiosk"))));

        if (run.RestorePoint is { } rp)
        {
            _body.Children.Add(Section("Point de restauration"));
            _body.Children.Add(Card(ReportRow(rp), 12));
        }
        if (run.Tweaks.Count > 0)
        {
            _body.Children.Add(Section($"Réglages ({run.Tweaks.Count(i => i.State == ItemState.Done)} sur {run.Tweaks.Count})"));
            _body.Children.Add(Card(ReportList(run.Tweaks.OrderBy(i => i.State == ItemState.Done ? 1 : 0))));
        }
        if (run.Apps.Count > 0)
        {
            _body.Children.Add(Section($"Applications ({run.Apps.Count(i => i.State == ItemState.Done)} sur {run.Apps.Count})"));
            _body.Children.Add(Card(ReportList(run.Apps.OrderBy(i => i.State == ItemState.Done ? 1 : 0))));
        }

        if (AppHost.Registry.GetPage("journal") is null && run.Tweaks.Any(i => i.State == ItemState.Done))
        {
            var back = Caption("Pour revenir en arrière sur un réglage, ouvrez sa page (Confidentialité, Performances, Sécurité…) " +
                               "et choisissez de nouveau l'option d'origine.", "Pp.TextTertiary");
            back.Margin = new Thickness(0, 8, 0, 0);
            _body.Children.Add(back);
        }
        else if (AppHost.Registry.GetPage("journal") is not null)
            NavLeft(Button("Ouvrir le journal (annuler)", "", "Pp.Button", (_, _) => AppHost.Navigator.Navigate("journal")));
        NavLeft(Button("Exporter ce plan…", "", "Pp.SubtleButton", (_, _) => _ = ExportAsync()));
        NavRight(Button("Terminer", "", "Pp.AccentButton", (_, _) => Finish()));
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
            ItemState.Failed => ("", "Pp.Danger", "Échec"),
            _ => ("", "Pp.TextTertiary", "Non effectué"),
        };
        var detail = label + (item.Detail is null ? "" : " : " + item.Detail);
        if (item.State != ItemState.Done && item.Message is { Length: > 0 } msg) detail += " — " + msg;
        else if (item.Tweak is null && item.Message is { Length: > 0 } ok) detail += " — " + ok; // application ou point de restauration
        var row = (DockPanel)SimpleRow(glyph, brush, item.Title, detail);
        var badges = new WrapPanel { Margin = new Thickness(22, 2, 0, 0) };
        if (item.State == ItemState.Done && item.Tweak is { } t)
        {
            if (!t.IsReversible) badges.Children.Add(Badge("Non annulable", "Danger"));
            if (t.Effect.HasFlag(ApplyEffect.Reboot)) badges.Children.Add(Badge("Redémarrage requis", "Info", ""));
            else if (t.Effect.HasFlag(ApplyEffect.SignOut)) badges.Children.Add(Badge("Déconnexion requise", "Info", ""));
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
        AppHost.Toasts.Show("Configuration terminée. Vous pouvez relancer l'assistant à tout moment.", ToastKind.Success);
    }
}
