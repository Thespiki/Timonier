using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Timonier.Core.Engine;
using Timonier.Core.Model;
using Timonier.Core.Platform;
using Timonier.UI.Controls;
using Timonier.UI.Services;

namespace Timonier.Modules.AppPages;

/// <summary>
/// Journal des modifications : entrées utilisateur (journal.json) et machine (HKLM), groupées par jour,
/// avec annulation unitaire ou groupée, filtres, recherche, export CSV.
/// </summary>
public sealed class JournalPage : UserControl, INavigationAware
{
    private static CultureInfo Culture => Core.Localization.Loc.Culture;
    private const int PageSize = 80;

    private readonly StackPanel _summary = new() { Orientation = Orientation.Horizontal };
    private readonly SegmentedBar _filter = new();
    private readonly TextBox _search;
    private readonly StackPanel _list = new();
    private readonly TextBlock _status;
    private readonly Button _undoSession, _undoToday, _export, _clear;

    private List<JournalEntry> _all = [];
    private string _query = "";
    private int _limit = PageSize;
    private bool _busy, _dirty = true, _visible, _loading;
    private readonly DateTime _sessionStart;

    public JournalPage()
    {
        try { using var p = Process.GetCurrentProcess(); _sessionStart = p.StartTime; }
        catch { _sessionStart = DateTime.Now; }

        var stack = PageScaffold.Create(this, L("Journal des modifications"),
            L("Tout ce que Timonier a modifié sur ce PC, avec l'état précédent pour revenir en arrière."), AppPagesModule.JournalGlyph);

        // Résumé + annulations groupées
        _undoSession = AppUi.Button(L("Annuler la session"), "", "Pp.Button", async (_, _) => await UndoManyAsync(session: true));
        _undoSession.ToolTip = L("Annule, de la plus récente à la plus ancienne, les modifications faites depuis l'ouverture de Timonier.");
        _undoToday = AppUi.Button(L("Annuler aujourd'hui"), "", "Pp.Button", async (_, _) => await UndoManyAsync(session: false));
        _undoToday.ToolTip = L("Annule, de la plus récente à la plus ancienne, toutes les modifications faites aujourd'hui.");
        _undoToday.Margin = new Thickness(8, 0, 0, 0);
        var undoButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        undoButtons.Children.Add(_undoSession);
        undoButtons.Children.Add(_undoToday);
        var summaryDock = new DockPanel();
        DockPanel.SetDock(undoButtons, Dock.Right);
        summaryDock.Children.Add(undoButtons);
        summaryDock.Children.Add(_summary);
        stack.Children.Add(AppUi.Card(summaryDock));

        // Barre d'outils
        _search = AppUi.SearchBox(L("Rechercher dans le journal (réglage, valeur, note…)"), q => { _query = q; _limit = PageSize; Render(); });
        _export = AppUi.Button(L("Exporter (CSV)"), "", "Pp.Button", async (_, _) => await ExportAsync());
        _export.ToolTip = L("Enregistre toutes les entrées du journal dans un fichier CSV (UTF-8, séparateur « ; »), lisible dans Excel.");
        _clear = AppUi.Button(L("Vider le journal utilisateur"), "", "Pp.SubtleButton", async (_, _) => await ClearUserJournalAsync());
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _export.Margin = new Thickness(8, 0, 0, 0);
        _clear.Margin = new Thickness(8, 0, 0, 0);
        right.Children.Add(_export);
        right.Children.Add(_clear);
        var toolbar = new DockPanel { Margin = new Thickness(0, 18, 0, 10) };
        DockPanel.SetDock(right, Dock.Right);
        toolbar.Children.Add(right);
        toolbar.Children.Add(new Border { Child = _search, MaxWidth = 440, HorizontalAlignment = HorizontalAlignment.Stretch });
        stack.Children.Add(toolbar);

        _filter.Add(L("Tout"), "");
        _filter.Add(L("Annulables"), "");
        _filter.Add(LC("badge", "Admin"), "");
        _filter.Add(L("Utilisateur"), "");
        _filter.Select(0, notify: false);
        _filter.SelectionChanged += (_, _) => { _limit = PageSize; Render(); };
        _filter.Margin = new Thickness(0, 0, 0, 6);
        stack.Children.Add(_filter);

        _status = AppUi.Caption("");
        _status.Margin = new Thickness(2, 6, 0, 4);
        _status.Visibility = Visibility.Collapsed;
        stack.Children.Add(_status);

        _list.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(_list);

        stack.Children.Add(PageScaffold.InfoBar(
            L("Les modifications faites sans droits d'administrateur sont enregistrées dans votre profil (journal.json). Celles faites par la session administrateur sont enregistrées dans HKLM\\SOFTWARE\\Timonier\\Journal, que seuls les administrateurs peuvent modifier : Timonier ne peut donc annuler que ce qu'il a réellement enregistré. Les actions ponctuelles (outils système) ne sont pas annulables."),
            ""));

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // ------------------------------------------------------------------ Cycle de vie

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _visible = true;
        AppHost.Engine.Changed += OnEngineChanged;
        if (_dirty) _ = ReloadAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _visible = false;
        AppHost.Engine.Changed -= OnEngineChanged;
    }

    private void OnEngineChanged(object? sender, TweakChangedEventArgs e) =>
        Dispatcher.InvokeAsync(() =>
        {
            _dirty = true;
            if (_visible && !_busy) _ = ReloadAsync();
        });

    public void OnNavigatedTo(object? parameter)
    {
        switch (parameter as string)
        {
            case "filter:undoable": _filter.Select(1); break;
            case "filter:admin": _filter.Select(2); break;
            case "filter:user": _filter.Select(3); break;
            case { } s when s.StartsWith("search:", StringComparison.Ordinal):
                _search.Text = s["search:".Length..];
                break;
        }
    }

    private async Task ReloadAsync()
    {
        if (_loading) return;
        _loading = true;
        _dirty = false;
        if (_all.Count == 0)
        {
            _list.Children.Clear();
            _list.Children.Add(AppUi.StateCard("", L("Lecture du journal…"), busy: true));
        }
        try
        {
            _all = await Task.Run(() => AppHost.Engine.JournalAll());
            Render();
        }
        catch (Exception ex)
        {
            Log.Error("AppPages", "lecture du journal", ex);
            _list.Children.Clear();
            var card = AppUi.StateCard("", L("Le journal n'a pas pu être lu"), ex.Message);
            var retry = AppUi.Button(L("Réessayer"), "", "Pp.Button", (_, _) => _ = ReloadAsync());
            retry.HorizontalAlignment = HorizontalAlignment.Center;
            retry.Margin = new Thickness(0, 10, 0, 0);
            ((StackPanel)card.Child).Children.Add(retry);
            _list.Children.Add(card);
        }
        finally
        {
            _loading = false;
            if (_dirty && _visible && !_busy) _ = ReloadAsync();
        }
    }

    // ------------------------------------------------------------------ Affichage

    private IEnumerable<JournalEntry> Filtered()
    {
        IEnumerable<JournalEntry> items = _filter.SelectedIndex switch
        {
            1 => _all.Where(e => e.CanUndo),
            2 => _all.Where(e => e.Machine),
            3 => _all.Where(e => !e.Machine),
            _ => _all,
        };
        if (_query.Length > 0) items = items.Where(Matches);
        return items;
    }

    private bool Matches(JournalEntry e)
    {
        bool Has(string? s) => s is not null &&
            Culture.CompareInfo.IndexOf(s, _query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
        return Has(e.Title) || Has(e.SourceId) || Has(e.ToLabel) || Has(e.Note) || Has(ChangeText(e));
    }

    private void Render()
    {
        UpdateSummary();
        _filter.SetCount(0, _all.Count.ToString(Culture));
        _filter.SetCount(1, _all.Count(e => e.CanUndo).ToString(Culture));
        _filter.SetCount(2, _all.Count(e => e.Machine).ToString(Culture));
        _filter.SetCount(3, _all.Count(e => !e.Machine).ToString(Culture));
        UpdateButtons();

        _list.Children.Clear();
        if (_all.Count == 0)
        {
            _list.Children.Add(AppUi.StateCard(AppPagesModule.JournalGlyph, L("Aucune modification pour l'instant"),
                L("Quand vous appliquez un réglage ou une action avec Timonier, la modification apparaît ici avec l'état précédent, pour pouvoir revenir en arrière à tout moment. Rien n'est modifié sans votre accord.")));
            return;
        }

        var items = Filtered().ToList();
        if (items.Count == 0)
        {
            _list.Children.Add(AppUi.StateCard("", L("Aucune entrée ne correspond"),
                _query.Length > 0 ? L("Aucun résultat pour « {0} » avec ce filtre.", _query) : L("Aucune entrée dans cette catégorie.")));
            return;
        }

        var shown = items.Take(_limit).ToList();
        foreach (var day in shown.GroupBy(e => e.At.LocalDateTime.Date))
        {
            var count = items.Count(e => e.At.LocalDateTime.Date == day.Key);
            var head = new DockPanel { Margin = new Thickness(2, 14, 2, 8) };
            var countText = AppUi.Caption(LP(count, "{0} modification", "{0} modifications"));
            countText.VerticalAlignment = VerticalAlignment.Bottom;
            DockPanel.SetDock(countText, Dock.Right);
            head.Children.Add(countText);
            var title = AppUi.Text(DayLabel(day.Key), "Pp.CardTitle", wrap: false);
            head.Children.Add(title);
            _list.Children.Add(head);

            var rows = new StackPanel();
            var first = true;
            foreach (var entry in day)
            {
                if (!first) rows.Children.Add(AppUi.Divider(new Thickness(0, 10, 0, 10)));
                first = false;
                rows.Children.Add(BuildRow(entry));
            }
            _list.Children.Add(AppUi.Card(rows));
        }

        if (items.Count > shown.Count)
        {
            var remaining = items.Count - shown.Count;
            var more = AppUi.Button(LP(remaining, "Afficher plus ({0} restante)", "Afficher plus ({0} restantes)"), "", "Pp.Button",
                (_, _) => { _limit += PageSize; Render(); });
            more.HorizontalAlignment = HorizontalAlignment.Center;
            more.Margin = new Thickness(0, 12, 0, 0);
            _list.Children.Add(more);
        }
    }

    private void UpdateSummary()
    {
        _summary.Children.Clear();
        var today = _all.Count(e => e.At.LocalDateTime.Date == DateTime.Today);
        var session = _all.Count(e => e.At.LocalDateTime >= _sessionStart);
        void Metric(string value, string label)
        {
            var s = new StackPanel { Margin = new Thickness(0, 0, 32, 0) };
            s.Children.Add(new TextBlock { Text = value }.Styled("Pp.Metric"));
            s.Children.Add(AppUi.Caption(label));
            _summary.Children.Add(s);
        }
        Metric(_all.Count.ToString(Culture), L("au total"));
        Metric(_all.Count(e => e.CanUndo).ToString(Culture), LC("journal metric", "annulables"));
        Metric(today.ToString(Culture), LC("journal metric", "aujourd'hui"));
        Metric(session.ToString(Culture), LC("journal metric", "depuis l'ouverture"));
    }

    private void UpdateButtons()
    {
        _undoSession.IsEnabled = !_busy && _all.Any(e => e.CanUndo && e.At.LocalDateTime >= _sessionStart);
        _undoToday.IsEnabled = !_busy && _all.Any(e => e.CanUndo && e.At.LocalDateTime.Date == DateTime.Today);
        _export.IsEnabled = !_busy && _all.Count > 0;
        _clear.IsEnabled = !_busy && _all.Any(e => !e.Machine);
    }

    private FrameworkElement BuildRow(JournalEntry e)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var time = AppUi.Text(e.At.LocalDateTime.ToString("t", Culture), "Pp.Caption", wrap: false);
        time.VerticalAlignment = VerticalAlignment.Top;
        time.Margin = new Thickness(0, 9, 0, 0);
        time.ToolTip = L("{0} à {1}", e.At.LocalDateTime.ToString("D", Culture), e.At.LocalDateTime.ToString("T", Culture));
        g.Children.Add(time);

        var tile = e.Undone
            ? AppUi.GlyphTile(SourceGlyph(e.SourceId), "Pp.NeutralBackground", "Pp.Neutral")
            : AppUi.GlyphTile(SourceGlyph(e.SourceId));
        tile.Margin = new Thickness(0, 0, 14, 0);
        Grid.SetColumn(tile, 1);
        g.Children.Add(tile);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = AppUi.Text(e.Title.Length > 0 ? e.Title : e.SourceId);
        title.FontWeight = FontWeights.SemiBold;
        if (e.Undone) title.Themed(TextBlock.ForegroundProperty, "Pp.TextSecondary");
        text.Children.Add(title);
        var change = ChangeText(e);
        if (change.Length > 0)
        {
            var c = AppUi.Caption(change);
            c.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(c);
        }
        var badges = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        void AddBadge(Border b) { b.Margin = new Thickness(0, 0, 6, 0); badges.Children.Add(b); }
        AddBadge(e.Machine ? AppUi.Badge(LC("badge", "Admin"), "Info", "") : AppUi.Badge(L("Utilisateur"), "Neutral", ""));
        if (e.Undone)
        {
            var at = e.UndoneAt?.LocalDateTime;
            AddBadge(AppUi.Badge(at is { } d ? L("Annulé le {0}", Format.Date(d)) : L("Annulé"), "Success", ""));
        }
        else if (!e.CanUndo)
            AddBadge(AppUi.Badge(LC("badge", "Non annulable"), "Warning", ""));
        if (e.Machine && !string.IsNullOrEmpty(e.UserSid) && !string.IsNullOrEmpty(AppHost.Profile.UserSid)
            && !string.Equals(e.UserSid, AppHost.Profile.UserSid, StringComparison.OrdinalIgnoreCase))
            AddBadge(AppUi.Badge(L("Autre compte"), "Neutral", ""));
        text.Children.Add(badges);
        if (!string.IsNullOrWhiteSpace(e.Note))
        {
            var n = AppUi.Caption(e.Note, tertiary: true);
            n.Margin = new Thickness(0, 6, 0, 0);
            text.Children.Add(n);
        }
        Grid.SetColumn(text, 2);
        g.Children.Add(text);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        if (AppHost.Registry.GetTweak(e.SourceId) is { } tweak)
        {
            var open = AppUi.Button("", "", "Pp.SubtleButton", (_, _) =>
                AppHost.Navigator.Navigate(AppHost.Registry.PageIdForCategory(tweak.Category), "tweak:" + tweak.Id));
            open.ToolTip = L("Ouvrir ce réglage");
            System.Windows.Automation.AutomationProperties.SetName(open, L("Ouvrir ce réglage"));
            open.Width = 34;
            open.Padding = new Thickness(0);
            actions.Children.Add(open);
        }
        if (e.CanUndo)
        {
            var undo = AppUi.Button(L("Annuler"), "", "Pp.Button");
            undo.Margin = new Thickness(6, 0, 0, 0);
            undo.ToolTip = e.Machine ? L("Annule via la session administrateur (une invite UAC peut s'afficher).") : L("Rétablit l'état précédent.");
            undo.IsEnabled = !_busy;
            undo.Click += async (_, _) => await UndoOneAsync(e, undo);
            actions.Children.Add(undo);
        }
        Grid.SetColumn(actions, 3);
        g.Children.Add(actions);
        return g;
    }

    // ------------------------------------------------------------------ Libellés

    private static string DayLabel(DateTime day)
    {
        var full = day.ToString("D", Culture);
        if (day == DateTime.Today) return L("Aujourd'hui — {0}", full);
        if (day == DateTime.Today.AddDays(-1)) return L("Hier — {0}", full);
        return char.ToUpper(full[0], Culture) + full[1..];
    }

    /// <summary>« Ancien libellé → nouveau libellé » (libellés du catalogue quand le réglage existe encore).</summary>
    internal static string ChangeText(JournalEntry e)
    {
        var tweak = AppHost.Registry.GetTweak(e.SourceId);
        var to = e.ToLabel ?? (e.ToOption is { } k ? tweak?.GetOption(k)?.Label ?? k : null);
        string? from = null;
        if (e.FromOption is { } f) from = tweak?.GetOption(f)?.Label ?? f;
        if (tweak?.Kind == TweakKind.Action) from = null;
        if (from is not null && to is not null) return $"{from} → {to}";
        return to ?? "";
    }

    private static string SourceGlyph(string sourceId)
    {
        var reg = AppHost.Registry;
        if (reg.GetTweak(sourceId) is { } t && reg.GetCategory(t.Category) is { Glyph.Length: > 0 } cat) return cat.Glyph;
        var prefix = sourceId.Split('.')[0];
        prefix = prefix switch { "custom" => "customization", "perf" => "performance", _ => prefix };
        if (reg.GetCategory(prefix) is { Glyph.Length: > 0 } c) return c.Glyph;
        if (reg.GetPage(prefix) is { Glyph.Length: > 0 } p) return p.Glyph;
        return AppPagesModule.JournalGlyph;
    }

    // ------------------------------------------------------------------ Annulation

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        _status.Text = status ?? "";
        _status.Visibility = string.IsNullOrEmpty(status) ? Visibility.Collapsed : Visibility.Visible;
        UpdateButtons();
        foreach (var b in FindButtons(_list)) b.IsEnabled = !busy;
    }

    private static IEnumerable<Button> FindButtons(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is Button b) yield return b;
            else foreach (var inner in FindButtons(child)) yield return inner;
        }
    }

    private async Task UndoOneAsync(JournalEntry entry, Button button)
    {
        if (_busy) return;
        SetBusy(true, L("Annulation : {0}…", entry.Title));
        AppUi.SetButtonText(button, L("Annulation…"));
        try
        {
            var outcome = await AppHost.Engine.UndoAsync(entry);
            AppHost.Toasts.ShowOutcome(outcome);
        }
        finally
        {
            SetBusy(false);
            await ReloadAsync();
        }
    }

    private async Task UndoManyAsync(bool session)
    {
        if (_busy) return;
        // Le journal administrateur est commun à tous les comptes : le broker refuse d'annuler l'entrée d'un autre compte.
        var mySid = AppHost.Profile.UserSid;
        var items = _all
            .Where(e => e.CanUndo && (session ? e.At.LocalDateTime >= _sessionStart : e.At.LocalDateTime.Date == DateTime.Today))
            .Where(e => !e.Machine || e.UserSid is null || string.Equals(e.UserSid, mySid, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.At)
            .ToList();
        if (items.Count == 0)
        {
            AppHost.Toasts.Show(session ? L("Aucune modification annulable depuis l'ouverture de Timonier.") : L("Aucune modification annulable aujourd'hui."),
                ToastKind.Info);
            return;
        }

        var content = new StackPanel { MaxWidth = 520 };
        content.Children.Add(AppUi.Text(session
            ? LP(items.Count, "La modification suivante, faite depuis l'ouverture de Timonier, sera annulée :",
                "Les {0} modifications suivantes, faites depuis l'ouverture de Timonier, seront annulées de la plus récente à la plus ancienne :")
            : LP(items.Count, "La modification suivante, faite aujourd'hui, sera annulée :",
                "Les {0} modifications suivantes, faites aujourd'hui, seront annulées de la plus récente à la plus ancienne :")));
        var list = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var e in items.Take(12))
        {
            var line = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var time = AppUi.Caption(e.At.LocalDateTime.ToString("t", Culture));
            time.Width = 48;
            DockPanel.SetDock(time, Dock.Left);
            line.Children.Add(time);
            var change = ChangeText(e);
            line.Children.Add(AppUi.Text(change.Length > 0 ? L("{0} ({1})", e.Title, change) : e.Title));
            list.Children.Add(line);
        }
        if (items.Count > 12) list.Children.Add(AppUi.Caption(LP(items.Count - 12, "… et {0} autre.", "… et {0} autres.")));
        content.Children.Add(list);
        if (items.Any(e => e.Machine))
        {
            var admin = AppUi.Caption(L("Certaines modifications ont été faites avec les droits d'administrateur : une invite UAC peut s'afficher. Si vous la refusez, l'annulation s'arrête là et le reste est conservé."));
            admin.Margin = new Thickness(0, 12, 0, 0);
            content.Children.Add(admin);
        }

        if (!await AppHost.Dialogs.ShowAsync(session ? L("Annuler les modifications de la session") : L("Annuler les modifications du jour"),
                content, L("Tout annuler"), L("Garder")))
            return;

        var done = 0;
        var failures = new List<string>();
        var effect = ApplyEffect.None;
        var stopped = false;
        SetBusy(true);
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                var e = items[i];
                SetBusy(true, L("Annulation {0} sur {1} : {2}…", i + 1, items.Count, e.Title));
                var outcome = await AppHost.Engine.UndoAsync(e);
                if (outcome.Cancelled) { stopped = true; break; }
                if (outcome.Success) { done++; effect |= outcome.Effect; }
                else failures.Add(L("{0} : {1}", e.Title, outcome.Message));
            }
        }
        finally
        {
            SetBusy(false);
            await ReloadAsync();
        }

        var remaining = items.Count - done - failures.Count;
        if (failures.Count == 0 && !stopped)
        {
            AppHost.Toasts.ShowOutcome(new ApplyOutcome(true,
                LP(done, "{0} modification annulée.", "{0} modifications annulées."), effect));
            return;
        }
        // Phrases complètes juxtaposées (chacune porte son propre pluriel).
        var parts = new List<string> { LP(done, "{0} modification annulée.", "{0} modifications annulées.") };
        if (stopped) parts.Add(LP(remaining, "{0} modification non traitée (annulation interrompue).", "{0} modifications non traitées (annulation interrompue)."));
        if (failures.Count > 0) parts.Add(LP(failures.Count, "{0} modification en échec.", "{0} modifications en échec."));
        var msg = string.Join(" ", parts);
        if (effect != ApplyEffect.None) AppHost.Toasts.ShowOutcome(new ApplyOutcome(true, msg, effect));
        else AppHost.Toasts.Show(msg, failures.Count > 0 ? ToastKind.Error : ToastKind.Warning);
        if (failures.Count > 0)
            await AppHost.Dialogs.AlertAsync(L("Annulations en échec"), string.Join(Environment.NewLine, failures.Take(10)));
    }

    // ------------------------------------------------------------------ Export / vidage

    private async Task ExportAsync()
    {
        if (_busy || _all.Count == 0) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = L("Exporter le journal de Timonier"),
            FileName = L("Timonier - journal {0}.csv", DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            DefaultExt = ".csv",
            Filter = L("Fichier CSV") + " (*.csv)|*.csv",
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var path = dialog.FileName;
        var rows = _all.ToList();
        SetBusy(true, L("Export en cours…"));
        try
        {
            var csv = await Task.Run(() => BuildCsv(rows));
            await File.WriteAllTextAsync(path, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            AppHost.Toasts.Show(LP(rows.Count, "Journal exporté : {0} entrée.", "Journal exporté : {0} entrées."), ToastKind.Success, L("Ouvrir le dossier"), () =>
            {
                try { if (Path.GetDirectoryName(path) is { } dir) ProcessRunner.OpenFolder(dir); }
                catch (Exception ex) { AppHost.Toasts.Show(L("Impossible d'ouvrir le dossier : {0}", ex.Message), ToastKind.Error); }
            });
        }
        catch (Exception ex)
        {
            Log.Error("AppPages", "export du journal", ex);
            AppHost.Toasts.Show(L("Export impossible : {0}", ex.Message), ToastKind.Error);
        }
        finally { SetBusy(false); }
    }

    private static string BuildCsv(IEnumerable<JournalEntry> rows)
    {
        static string Esc(string? s)
        {
            s ??= "";
            // Neutralise les formules (injection CSV dans un tableur). Le titre et la note viennent de journal.json,
            // modifiable sans élévation : on couvre aussi tabulation et retour chariot en tête (règles OWASP).
            if (s.Length > 0 && s[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n') s = "'" + s;
            return s.IndexOfAny([';', '"', '\n', '\r', '\t']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }
        var sb = new StringBuilder();
        sb.AppendLine(L("Date;Heure;Titre;Identifiant;Modification;Portée;Annulable;Annulé le;Note"));
        foreach (var e in rows)
        {
            var at = e.At.LocalDateTime;
            sb.Append(Esc(at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).Append(';')
              .Append(Esc(at.ToString("HH:mm:ss", CultureInfo.InvariantCulture))).Append(';')
              .Append(Esc(e.Title)).Append(';')
              .Append(Esc(e.SourceId)).Append(';')
              .Append(Esc(ChangeText(e))).Append(';')
              .Append(e.Machine ? LC("badge", "Admin") : L("Utilisateur")).Append(';')
              .Append(e.CanUndo ? L("Oui") : L("Non")).Append(';')
              .Append(Esc(e.UndoneAt?.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))).Append(';')
              .Append(Esc(e.Note?.ReplaceLineEndings(" ")))
              .AppendLine();
        }
        return sb.ToString();
    }

    private async Task ClearUserJournalAsync()
    {
        if (_busy) return;
        var user = _all.Where(e => !e.Machine).ToList();
        if (user.Count == 0) return;
        var undoable = user.Count(e => e.CanUndo);
        var machine = _all.Count - user.Count;
        // Paragraphes complets juxtaposés (chacun porte son propre pluriel).
        var message = new StringBuilder();
        message.Append(LP(user.Count, "L'entrée du journal utilisateur sera effacée.", "Les {0} entrées du journal utilisateur seront effacées."));
        if (undoable > 0)
            message.Append(' ').Append(LP(undoable,
                "{0} d'entre elles est encore annulable : la modification restera en place, mais Timonier ne pourra plus l'annuler automatiquement.",
                "{0} d'entre elles sont encore annulables : les modifications resteront en place, mais Timonier ne pourra plus les annuler automatiquement."));
        message.Append("\n\n");
        message.Append(machine > 0
            ? LP(machine,
                "Le journal administrateur ({0} entrée) est conservé dans HKLM\\SOFTWARE\\Timonier\\Journal : il est géré par la session administrateur, qui en garde les 500 entrées les plus récentes, et ne peut pas être modifié depuis l'interface sans droits d'administrateur.",
                "Le journal administrateur ({0} entrées) est conservé dans HKLM\\SOFTWARE\\Timonier\\Journal : il est géré par la session administrateur, qui en garde les 500 entrées les plus récentes, et ne peut pas être modifié depuis l'interface sans droits d'administrateur.")
            : L("Le journal administrateur est conservé dans HKLM\\SOFTWARE\\Timonier\\Journal : il est géré par la session administrateur, qui en garde les 500 entrées les plus récentes, et ne peut pas être modifié depuis l'interface sans droits d'administrateur."));
        if (!await AppHost.Dialogs.ConfirmAsync(L("Vider le journal utilisateur ?"), message.ToString(), L("Vider"), L("Annuler"), danger: true))
            return;
        try
        {
            await Task.Run(JournalWriter.UserStore.Clear);
            AppHost.Toasts.Show(L("Journal utilisateur vidé."), ToastKind.Success);
        }
        catch (Exception ex)
        {
            Log.Error("AppPages", "vidage du journal", ex);
            AppHost.Toasts.Show(L("Impossible de vider le journal : {0}", ex.Message), ToastKind.Error);
        }
        await ReloadAsync();
    }
}
