using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Timonier.Core.Platform;
using Timonier.UI.Services;

namespace Timonier.Modules.Maintenance;

/// <summary>Section « Points de restauration » : état de la protection, création, liste (admin), activation.</summary>
internal sealed class RestorePanel
{
    public FrameworkElement Root { get; }

    private readonly TextBlock _statusTitle = MaintUi.Text("", "Pp.CardTitle");
    private readonly TextBlock _statusDetail = MaintUi.Text("", "Pp.Caption");
    private readonly Border _statusTile;
    private readonly TextBlock _statusIcon;
    private readonly Button _create, _list, _enable;
    private readonly ProgressBar _busyBar = MaintUi.BusyBar(100);
    private readonly TextBlock _busyText = MaintUi.Text("", "Pp.Caption");
    private readonly StackPanel _listHost = new() { Visibility = Visibility.Collapsed };
    private bool _busy;
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public RestorePanel()
    {
        _statusIcon = new TextBlock { FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.Icon");
        _statusTile = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(8), Child = _statusIcon, VerticalAlignment = VerticalAlignment.Top };

        var statusTexts = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        statusTexts.Children.Add(_statusTitle);
        _statusDetail.Margin = new Thickness(0, 3, 0, 0);
        statusTexts.Children.Add(_statusDetail);
        var status = new DockPanel();
        DockPanel.SetDock(_statusTile, Dock.Left);
        status.Children.Add(_statusTile);
        status.Children.Add(statusTexts);

        _create = MaintUi.Button("Créer un point de restauration", "", "Pp.AccentButton", async (_, _) => await CreateAsync());
        _list = MaintUi.Button("Afficher les points existants", "", "Pp.Button", async (_, _) => await LoadListAsync());
        _enable = MaintUi.Button("Activer la protection", "", "Pp.Button", async (_, _) => await EnableAsync());
        var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        foreach (var b in new[] { _create, _list, _enable })
        {
            b.Margin = new Thickness(0, 0, 8, 8);
            buttons.Children.Add(b);
        }
        var busyRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        // La ligne de progression n'occupe de place que pendant une opération.
        busyRow.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding(nameof(UIElement.Visibility)) { Source = _busyBar });
        busyRow.Children.Add(_busyBar);
        _busyText.Margin = new Thickness(10, 0, 0, 0);
        busyRow.Children.Add(_busyText);

        var tools = new WrapPanel();
        tools.Children.Add(MaintUi.Button("Ouvrir la restauration du système", "", "Pp.LinkButton", (_, _) => MaintUi.OpenTool(SystemTool.Rstrui)));
        tools.Children.Add(MaintUi.Button("Paramètres de protection du système", "", "Pp.LinkButton", (_, _) => MaintUi.OpenTool(SystemTool.SystemPropertiesProtection)));
        foreach (FrameworkElement link in tools.Children) link.Margin = new Thickness(0, 0, 20, 0);

        var body = new StackPanel();
        body.Children.Add(status);
        body.Children.Add(buttons);
        body.Children.Add(busyRow);
        body.Children.Add(_listHost);
        body.Children.Add(MaintUi.Divider(new Thickness(0, 8, 0, 8)));
        var limit = MaintUi.Text("Windows crée au plus un point de restauration par tranche de 24 heures : si un point récent existe déjà, la demande est ignorée (le point existant reste utilisable). " +
                                 "La liste des points et la création demandent une autorisation administrateur.", "Pp.Caption");
        body.Children.Add(limit);
        body.Children.Add(tools);

        Root = MaintUi.Card(body);
    }

    /// <summary>Lecture instantanée du registre (sans droits).</summary>
    public void RefreshStatus()
    {
        var policy = RestoreStatus.DisabledByPolicy;
        var enabled = RestoreStatus.ProtectionEnabled;
        (string glyph, string fg, string bg, string title, string detail) = (policy, enabled) switch
        {
            (true, _) => ("", "Pp.Danger", "Pp.DangerBackground", "Restauration du système désactivée par l'organisation",
                "Une stratégie empêche la création de points de restauration sur ce PC."),
            (_, true) => ("", "Pp.Success", "Pp.SuccessBackground", "Protection du système activée",
                "Windows crée aussi des points automatiquement avant l'installation de mises à jour ou de pilotes."),
            (_, false) => ("", "Pp.Warning", "Pp.WarningBackground", "Protection du système désactivée",
                "Aucun point de restauration ne peut être créé : activez la protection du lecteur système."),
            _ => ("", "Pp.Info", "Pp.InfoBackground", "État de la protection du système inconnu",
                "Affichez les points existants ou ouvrez les paramètres de protection pour vérifier."),
        };
        _statusIcon.Text = glyph;
        _statusIcon.SetResourceReference(TextBlock.ForegroundProperty, fg);
        _statusTile.SetResourceReference(Border.BackgroundProperty, bg);
        _statusTitle.Text = title;
        _statusDetail.Text = detail;
        _enable.Visibility = !policy && enabled != true ? Visibility.Visible : Visibility.Collapsed;
        _create.IsEnabled = !_busy && !policy;
    }

    private void SetBusy(bool busy, string text = "")
    {
        _busy = busy;
        _create.IsEnabled = _list.IsEnabled = _enable.IsEnabled = !busy;
        _busyBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _busyText.Text = text;
        if (!busy) RefreshStatus();
    }

    private async Task CreateAsync()
    {
        if (_busy) return;
        var description = await AppHost.Dialogs.PromptAsync("Créer un point de restauration",
            "Donnez un nom à ce point pour le reconnaître plus tard (64 caractères au plus).", "Timonier",
            validate: RestorePointCreateAction.CheckDescription);
        if (description is null) return;
        SetBusy(true, "Création du point de restauration…");
        try
        {
            var outcome = await AppHost.Engine.RunActionAsync(RestorePointCreateAction.ActionId,
                new Dictionary<string, string> { ["description"] = description.Trim() }, new Progress<string>(s => _busyText.Text = s));
            AppHost.Toasts.ShowOutcome(outcome);
            if (outcome.Success && _listHost.Visibility == Visibility.Visible)
            {
                SetBusy(false);
                await LoadListAsync();
            }
        }
        finally
        {
            if (_busy) SetBusy(false);
        }
    }

    private async Task EnableAsync()
    {
        if (_busy) return;
        SetBusy(true, "Activation de la protection du système…");
        try
        {
            var outcome = await AppHost.Engine.RunActionAsync(RestoreEnableAction.ActionId, null, new Progress<string>(s => _busyText.Text = s));
            AppHost.Toasts.ShowOutcome(outcome);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadListAsync()
    {
        if (_busy) return;
        SetBusy(true, "Lecture des points de restauration…");
        try
        {
            var outcome = await AppHost.Engine.RunActionAsync(RestorePointListAction.ActionId);
            _listHost.Children.Clear();
            _listHost.Visibility = Visibility.Visible;
            if (!outcome.Success || outcome.Data is not { } d)
            {
                _listHost.Children.Add(MaintUi.Text(outcome.Cancelled ? "Liste non affichée : autorisation administrateur refusée." : "Impossible de lire les points : " + outcome.Message, "Pp.Caption"));
                return;
            }
            var count = int.TryParse(d.GetValueOrDefault("count"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) ? c : 0;
            var title = MaintUi.Text(count == 0 ? "Aucun point de restauration" : count == 1 ? "1 point de restauration" : $"{count} points de restauration", "Pp.CardTitle");
            title.Margin = new Thickness(0, 6, 0, 6);
            _listHost.Children.Add(title);
            if (count == 0)
            {
                _listHost.Children.Add(MaintUi.Text("Créez-en un maintenant pour pouvoir revenir en arrière en cas de problème.", "Pp.Caption"));
                return;
            }
            for (var i = 0; i < count; i++)
            {
                var date = DateTime.TryParse(d.GetValueOrDefault($"{i}.date"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt : (DateTime?)null;
                var type = int.TryParse(d.GetValueOrDefault($"{i}.type"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) ? t : 0;
                _listHost.Children.Add(BuildPointRow(d.GetValueOrDefault($"{i}.desc") ?? "", date, type, i == 0));
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static FrameworkElement BuildPointRow(string description, DateTime? date, int type, bool newest)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var when = MaintUi.Text(date is { } d ? d.ToLocalTime().ToString("ddd d MMM yyyy, HH:mm", Fr) : "Date inconnue", "Pp.Body", wrap: false);
        var desc = MaintUi.Text(description.Length == 0 ? "(sans description)" : description, "Pp.Body");
        desc.TextTrimming = TextTrimming.CharacterEllipsis;
        desc.TextWrapping = TextWrapping.NoWrap;
        desc.ToolTip = description;
        Grid.SetColumn(desc, 1);
        var badge = newest
            ? MaintUi.Badge("Le plus récent", "Pp.AccentText", "Pp.AccentSubtle")
            : MaintUi.Badge(RestorePoints.TypeLabel(type));
        badge.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(badge, 2);
        grid.Children.Add(when);
        grid.Children.Add(desc);
        grid.Children.Add(badge);
        var row = new Border { Child = grid, Padding = new Thickness(10, 7, 10, 7), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 0, 4) };
        row.SetResourceReference(Border.BackgroundProperty, "Pp.CardSecondary");
        return row;
    }
}
