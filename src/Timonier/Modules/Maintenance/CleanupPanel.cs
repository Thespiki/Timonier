using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Timonier.Core.Platform;
using Timonier.UI.Services;

namespace Timonier.Modules.Maintenance;

/// <summary>Section « Nettoyage » : analyse par catégorie (taille, nombre de fichiers), sélection, nettoyage et bilan.</summary>
internal sealed class CleanupPanel
{
    public FrameworkElement Root { get; }
    /// <summary>Total estimé (octets, « au moins » si partiel) après chaque analyse.</summary>
    public event Action<long, bool>? Estimated;

    private sealed class Row
    {
        public required CleanupCategory Category { get; init; }
        public required CheckBox Check { get; init; }
        public required TextBlock Size { get; init; }
        public required TextBlock Count { get; init; }
        public CleanupStats? Stats { get; set; }
    }

    private readonly List<Row> _rows = [];
    private readonly Button _analyze, _clean, _measureAdmin;
    private readonly ProgressBar _busyBar = MaintUi.BusyBar();
    private readonly TextBlock _status = MaintUi.Text("", "Pp.Caption");
    private readonly TextBlock _selection = MaintUi.Text("", "Pp.Body");
    private readonly Border _resultBar;
    private readonly TextBlock _resultText, _resultIcon;
    private bool _busy;

    public CleanupPanel()
    {
        var root = new StackPanel();
        var card = new StackPanel();

        foreach (var c in CleanupCatalog.All)
        {
            if (_rows.Count > 0) card.Children.Add(MaintUi.Divider(new Thickness(0, 10, 0, 10)));
            card.Children.Add(BuildRow(c));
        }

        // Pied de carte : sélection + boutons.
        _analyze = MaintUi.Button("Analyser", "", "Pp.Button", async (_, _) => await AnalyzeAsync());
        _clean = MaintUi.Button("Nettoyer la sélection", "", "Pp.AccentButton", async (_, _) => await CleanAsync());
        _clean.Margin = new Thickness(8, 0, 0, 0);
        _measureAdmin = MaintUi.Button("Mesurer les éléments système", "", "Pp.LinkButton", async (_, _) => await MeasureAdminAsync());
        _measureAdmin.ToolTip = "Mesure les dossiers de Windows illisibles sans droits administrateur (une autorisation sera demandée).";

        var footer = new DockPanel { Margin = new Thickness(0, 16, 0, 0), LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_analyze);
        buttons.Children.Add(_clean);
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var selRow = new StackPanel { Orientation = Orientation.Horizontal };
        selRow.Children.Add(_selection);
        _busyBar.Margin = new Thickness(12, 0, 0, 0);
        selRow.Children.Add(_busyBar);
        left.Children.Add(selRow);
        left.Children.Add(_status);
        footer.Children.Add(left);

        card.Children.Add(MaintUi.Divider(new Thickness(0, 14, 0, 0)));
        card.Children.Add(footer);
        var measureRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        measureRow.Children.Add(_measureAdmin);
        card.Children.Add(measureRow);

        (_resultBar, _resultText, _resultIcon) = MaintUi.InfoBar("Pp.InfoBar.Success", "");
        _resultBar.Margin = new Thickness(0, 0, 0, 10);
        root.Children.Add(_resultBar);
        root.Children.Add(MaintUi.Card(card));

        var links = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        links.Children.Add(MaintUi.Button("Stockage dans les Paramètres", "", "Pp.LinkButton", (_, _) => MaintUi.OpenSettings("ms-settings:storagesense")));
        links.Children.Add(MaintUi.Button("Nettoyage de disque de Windows", "", "Pp.LinkButton", (_, _) => MaintUi.OpenTool(SystemTool.CleanMgr)));
        foreach (FrameworkElement link in links.Children) link.Margin = new Thickness(0, 0, 20, 0);
        root.Children.Add(links);

        Root = root;
        UpdateSelection();
    }

    private FrameworkElement BuildRow(CleanupCategory c)
    {
        var texts = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new WrapPanel();
        var title = MaintUi.Text(c.Title, "Pp.CardTitle", wrap: false);
        title.Margin = new Thickness(0, 0, 8, 0);
        titleRow.Children.Add(title);
        if (c.Admin) titleRow.Children.Add(MaintUi.Badge("Administrateur", "Pp.TextSecondary", "Pp.CardSecondary", ""));
        texts.Children.Add(titleRow);
        var desc = MaintUi.Text(c.Description, "Pp.Caption");
        desc.Margin = new Thickness(0, 3, 0, 0);
        texts.Children.Add(desc);
        if (c.Note is { } note)
        {
            var n = MaintUi.Text(note, "Pp.Caption");
            n.Margin = new Thickness(0, 3, 0, 0);
            n.SetResourceReference(TextBlock.ForegroundProperty, "Pp.Warning");
            texts.Children.Add(n);
        }

        var content = new DockPanel();
        var tile = MaintUi.IconTile(c.Glyph, c.Admin ? "Pp.TextSecondary" : "Pp.AccentText", c.Admin ? "Pp.CardSecondary" : "Pp.AccentSubtle", 34);
        DockPanel.SetDock(tile, Dock.Left);
        content.Children.Add(tile);
        content.Children.Add(texts);

        var check = new CheckBox
        {
            IsChecked = c.DefaultSelected, Content = content, VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        System.Windows.Automation.AutomationProperties.SetName(check, c.Title);
        check.Checked += (_, _) => UpdateSelection();
        check.Unchecked += (_, _) => UpdateSelection();

        var size = MaintUi.Text("…", "Pp.CardTitle", wrap: false);
        size.HorizontalAlignment = HorizontalAlignment.Right;
        var count = MaintUi.Text("", "Pp.Caption", wrap: false);
        count.HorizontalAlignment = HorizontalAlignment.Right;
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MinWidth = 130, Margin = new Thickness(16, 0, 0, 0) };
        right.Children.Add(size);
        right.Children.Add(count);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(check);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        _rows.Add(new Row { Category = c, Check = check, Size = size, Count = count });
        return grid;
    }

    private void SetBusy(bool busy, string status = "")
    {
        _busy = busy;
        _analyze.IsEnabled = _clean.IsEnabled = _measureAdmin.IsEnabled = !busy;
        foreach (var r in _rows) r.Check.IsEnabled = !busy;
        _busyBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _status.Text = status;
        if (!busy) UpdateSelection();
    }

    private void UpdateSelection()
    {
        var selected = _rows.Where(r => r.Check.IsChecked == true).ToList();
        var known = selected.Where(r => r.Stats is { AccessDenied: false }).Sum(r => r.Stats!.Bytes);
        var unknown = selected.Any(r => r.Stats is null || r.Stats.AccessDenied);
        _selection.Text = selected.Count == 0
            ? "Aucune catégorie sélectionnée"
            : $"{selected.Count} catégorie(s) · {(unknown ? "au moins " : "")}{Format.Bytes(known)}";
        _clean.IsEnabled = !_busy && selected.Count > 0;
    }

    private static void ShowStats(Row row)
    {
        var s = row.Stats;
        if (s is null) { row.Size.Text = "…"; row.Count.Text = ""; return; }
        if (s.AccessDenied && s.Bytes == 0)
        {
            row.Size.Text = "—";
            row.Count.Text = row.Category.Admin ? "taille visible en admin" : "illisible";
            return;
        }
        row.Size.Text = (s.Partial || s.AccessDenied ? "≥ " : "") + Format.Bytes(s.Bytes);
        row.Count.Text = row.Category.Kind == CleanupKind.RecycleBin
            ? $"{s.Files.ToString("N0", CultureInfo.GetCultureInfo("fr-FR"))} élément(s)"
            : $"{s.Files.ToString("N0", CultureInfo.GetCultureInfo("fr-FR"))} fichier(s)";
    }

    // ================================================================== Analyse

    public async Task AnalyzeAsync()
    {
        if (_busy) return;
        SetBusy(true, "Analyse en cours…");
        try
        {
            foreach (var row in _rows)
            {
                _status.Text = "Analyse : " + row.Category.Title + "…";
                var budget = TimeSpan.FromSeconds(row.Category.Key == "usertemp" ? 10 : 5);
                try
                {
                    row.Stats = await Task.Run(() => CleanupEngine.Analyze(row.Category, budget, CancellationToken.None));
                }
                catch (Exception ex)
                {
                    Log.Warn("Maintenance", $"analyse {row.Category.Key} : {ex.Message}");
                    row.Stats = new CleanupStats { AccessDenied = true };
                }
                ShowStats(row);
            }
            RaiseEstimate();
        }
        finally
        {
            SetBusy(false, $"Analyse effectuée à {DateTime.Now:HH:mm}.");
        }
    }

    private void RaiseEstimate()
    {
        var measured = _rows.Where(r => r.Stats is not null).ToList();
        var total = measured.Sum(r => r.Stats!.Bytes);
        var partial = _rows.Any(r => r.Stats is null || r.Stats.Partial || r.Stats.AccessDenied);
        Estimated?.Invoke(total, partial);
    }

    private async Task MeasureAdminAsync()
    {
        if (_busy) return;
        SetBusy(true, "Mesure des dossiers système (autorisation administrateur)…");
        try
        {
            var p = new Dictionary<string, string> { ["categories"] = string.Join(",", CleanupCatalog.AdminKeys), ["mode"] = "analyze" };
            var outcome = await AppHost.Engine.RunActionAsync(CleanupRunAction.ActionId, p, new Progress<string>(s => _status.Text = s));
            if (!outcome.Success || outcome.Data is null)
            {
                AppHost.Toasts.ShowOutcome(outcome);
                return;
            }
            foreach (var row in _rows.Where(r => r.Category.Admin))
            {
                var d = outcome.Data;
                row.Stats = new CleanupStats
                {
                    Bytes = Long(d, row.Category.Key + ".bytes"),
                    Files = Long(d, row.Category.Key + ".files"),
                    Partial = d.ContainsKey(row.Category.Key + ".partial"),
                };
                ShowStats(row);
            }
            RaiseEstimate();
        }
        finally
        {
            SetBusy(false, "");
        }
    }

    private static long Long(Dictionary<string, string> d, string key) =>
        d.TryGetValue(key, out var v) && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : 0;

    // ================================================================== Nettoyage

    private async Task CleanAsync()
    {
        if (_busy) return;
        var selected = _rows.Where(r => r.Check.IsChecked == true).ToList();
        if (selected.Count == 0) return;

        var lines = string.Join("\n", selected.Select(r => "• " + r.Category.Title +
            (r.Stats is { AccessDenied: false } s ? $" ({Format.Bytes(s.Bytes)})" : "")));
        var warnings = new List<string>();
        if (selected.Any(r => r.Category.Kind == CleanupKind.RecycleBin)) warnings.Add("Le contenu de la corbeille sera supprimé définitivement.");
        if (selected.Any(r => r.Category.Key == "sysdumps")) warnings.Add("Les vidages mémoire ne pourront plus servir à diagnostiquer un écran bleu.");
        if (selected.Any(r => r.Category.Admin)) warnings.Add("Une autorisation administrateur sera demandée pour les éléments système.");
        var ok = await AppHost.Dialogs.ConfirmAsync("Nettoyer la sélection",
            "Éléments à nettoyer :\n" + lines + (warnings.Count > 0 ? "\n\n" + string.Join("\n", warnings) : "") +
            "\n\nLes fichiers en cours d'utilisation sont ignorés.", "Nettoyer", "Annuler", danger: selected.Any(r => r.Category.Kind == CleanupKind.RecycleBin));
        if (!ok) return;

        SetBusy(true, "Nettoyage en cours…");
        _resultBar.Visibility = Visibility.Collapsed;
        using var keepAlive = AppHost.Background.Acquire("Nettoyage en cours");
        long freed = 0, files = 0, skipped = 0;
        var errors = new List<string>();
        try
        {
            foreach (var row in selected.Where(r => !r.Category.Admin))
            {
                _status.Text = "Nettoyage : " + row.Category.Title + "…";
                try
                {
                    var s = await Task.Run(() => CleanupEngine.Clean(row.Category, TimeSpan.FromMinutes(3), CancellationToken.None));
                    freed += s.Bytes; files += s.Files; skipped += s.Skipped;
                    if (s.Note is { } note) errors.Add(row.Category.Title + " : " + note);
                }
                catch (Exception ex)
                {
                    Log.Warn("Maintenance", $"nettoyage {row.Category.Key} : {ex.Message}");
                    errors.Add(row.Category.Title + " : " + ex.Message);
                }
            }

            var adminKeys = selected.Where(r => r.Category.Admin).Select(r => r.Category.Key).ToList();
            if (adminKeys.Count > 0)
            {
                _status.Text = "Nettoyage des éléments système…";
                var outcome = await AppHost.Engine.RunActionAsync(CleanupRunAction.ActionId,
                    new Dictionary<string, string> { ["categories"] = string.Join(",", adminKeys) }, new Progress<string>(s => _status.Text = s));
                if (outcome.Success && outcome.Data is { } d)
                {
                    freed += Long(d, "total.bytes"); files += Long(d, "total.files"); skipped += Long(d, "total.skipped");
                    foreach (var key in adminKeys)
                        if (d.TryGetValue(key + ".note", out var note))
                            errors.Add(CleanupCatalog.Get(key)!.Title + " : " + note);
                }
                else if (outcome.Cancelled)
                {
                    errors.Add("Éléments système non nettoyés (autorisation refusée).");
                }
                else
                {
                    errors.Add("Éléments système : " + outcome.Message);
                }
            }
        }
        finally
        {
            SetBusy(false, "");
        }

        var fr = CultureInfo.GetCultureInfo("fr-FR");
        var summary = $"{Format.Bytes(freed)} libérés · {files.ToString("N0", fr)} élément(s) supprimé(s)" +
                      (skipped > 0 ? $" · {skipped.ToString("N0", fr)} ignoré(s) car en cours d'utilisation" : "") + ".";
        if (errors.Count > 0) summary += "\n" + string.Join("\n", errors);
        MaintUi.SetInfo(_resultBar, _resultText, _resultIcon, summary,
            errors.Count > 0 ? "Pp.InfoBar.Warning" : "Pp.InfoBar.Success", errors.Count > 0 ? "" : "");
        AppHost.Toasts.Show($"Nettoyage terminé : {Format.Bytes(freed)} libérés.", errors.Count > 0 ? ToastKind.Warning : ToastKind.Success);
        await AnalyzeAsync();
    }
}
