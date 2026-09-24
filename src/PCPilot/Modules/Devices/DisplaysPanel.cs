using System.Windows;
using System.Windows.Controls;
using PcPilot.Core.Platform;

namespace PcPilot.Modules.Devices;

/// <summary>Écrans actifs : résolution, fréquence, orientation ; signale un écran capable d'une fréquence plus élevée.</summary>
internal sealed class DisplaysPanel : UserControl
{
    private readonly StackPanel _rows = new();

    public event Action<string>? SummaryChanged;

    public DisplaysPanel()
    {
        Focusable = false;
        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var open = DevUi.Button("Paramètres d'affichage", "", "Pp.SubtleButton", (_, _) => Open("ms-settings:display"));
        DockPanel.SetDock(open, Dock.Right);
        footer.Children.Add(open);
        footer.Children.Add(new TextBlock
        {
            Text = "Résolution, échelle, disposition et fréquence se modifient dans les Paramètres d'affichage.",
            VerticalAlignment = VerticalAlignment.Center,
        }.Styled("Pp.Caption"));
        var stack = new StackPanel();
        stack.Children.Add(_rows);
        stack.Children.Add(DevUi.Divider(new Thickness(0, 6, 0, 0)));
        stack.Children.Add(footer);
        Content = DevUi.Card(stack);
        _rows.Children.Add(DevUi.Caption("Lecture des écrans…"));
    }

    public async Task LoadAsync()
    {
        List<DisplayInfo> displays;
        try { displays = await Task.Run(DisplayService.Load); }
        catch (Exception ex)
        {
            Log.Warn("Devices", "énumération des écrans : " + ex.Message);
            displays = [];
        }
        _rows.Children.Clear();
        if (displays.Count == 0)
        {
            _rows.Children.Add(DevUi.Text("Aucun écran actif n'a pu être lu."));
            SummaryChanged?.Invoke("Indisponible");
            return;
        }
        var first = true;
        foreach (var d in displays)
        {
            if (!first) _rows.Children.Add(DevUi.Divider(new Thickness(0, 4, 0, 4)));
            first = false;
            _rows.Children.Add(BuildRow(d, displays.Count));
        }
        var main = displays[0];
        SummaryChanged?.Invoke((displays.Count > 1 ? $"{displays.Count} écrans · " : "") + $"{main.Width} × {main.Height} à {main.RefreshHz} Hz");
    }

    private static FrameworkElement BuildRow(DisplayInfo d, int count)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tile = DevUi.IconTile("", 34);
        tile.Margin = new Thickness(0, 0, 14, 0);
        tile.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(tile);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(DevUi.Text(d.MonitorName));
        if (d.Primary && count > 1)
        {
            var b = DevUi.Badge("Principal", "Pp.Info");
            b.Margin = new Thickness(8, 0, 0, 0);
            title.Children.Add(b);
        }
        text.Children.Add(title);
        text.Children.Add(DevUi.Caption($"{d.Width} × {d.Height} · {d.RefreshHz} Hz · {d.OrientationLabel} · couleurs {d.BitsPerPixel} bits · carte : {d.AdapterName}"));
        if (d.MaxRefreshAtCurrent > d.RefreshHz)
        {
            var hint = DevUi.Caption($"Cet écran accepte jusqu'à {d.MaxRefreshAtCurrent} Hz dans cette résolution : une fréquence plus élevée rend l'affichage plus fluide " +
                                     "(consommation un peu plus élevée sur batterie).");
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Pp.AccentText");
            hint.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(hint);
        }
        else if ((long)d.MaxWidth * d.MaxHeight > (long)d.Width * d.Height)
        {
            text.Children.Add(DevUi.Caption($"Résolution maximale proposée : {d.MaxWidth} × {d.MaxHeight}."));
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var metric = new TextBlock { Text = $"{d.RefreshHz} Hz", VerticalAlignment = VerticalAlignment.Center, FontSize = 18 }.Styled("Pp.Metric");
        metric.FontSize = 18;
        Grid.SetColumn(metric, 2);
        grid.Children.Add(metric);
        return grid;
    }

    private static void Open(string uri)
    {
        try { ProcessRunner.OpenSettingsUri(uri); }
        catch (Exception ex) { Log.Warn("Devices", "ouverture des Paramètres : " + ex.Message); }
    }
}
