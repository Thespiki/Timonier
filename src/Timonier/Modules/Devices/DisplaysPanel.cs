using System.Windows;
using System.Windows.Controls;
using Timonier.Core.Platform;

namespace Timonier.Modules.Devices;

/// <summary>Écrans actifs : résolution, fréquence, orientation ; signale un écran capable d'une fréquence plus élevée.</summary>
internal sealed class DisplaysPanel : UserControl
{
    private readonly StackPanel _rows = new();

    public event Action<string>? SummaryChanged;

    public DisplaysPanel()
    {
        Focusable = false;
        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var open = DevUi.Button(L("Paramètres d'affichage"), "", "Pp.SubtleButton", (_, _) => Open("ms-settings:display"));
        DockPanel.SetDock(open, Dock.Right);
        footer.Children.Add(open);
        footer.Children.Add(new TextBlock
        {
            Text = L("Résolution, échelle, disposition et fréquence se modifient dans les Paramètres d'affichage."),
            VerticalAlignment = VerticalAlignment.Center,
        }.Styled("Pp.Caption"));
        var stack = new StackPanel();
        stack.Children.Add(_rows);
        stack.Children.Add(DevUi.Divider(new Thickness(0, 6, 0, 0)));
        stack.Children.Add(footer);
        Content = DevUi.Card(stack);
        _rows.Children.Add(DevUi.Caption(L("Lecture des écrans…")));
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
            _rows.Children.Add(DevUi.Text(L("Aucun écran actif n'a pu être lu.")));
            SummaryChanged?.Invoke(L("Indisponible"));
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
        SummaryChanged?.Invoke((displays.Count > 1 ? LP(displays.Count, "{0} écran", "{0} écrans") + " · " : "") + L("{0} × {1} à {2} Hz", main.Width, main.Height, main.RefreshHz));
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
            var b = DevUi.Badge(LC("display", "Principal"), "Pp.Info");
            b.Margin = new Thickness(8, 0, 0, 0);
            title.Children.Add(b);
        }
        text.Children.Add(title);
        text.Children.Add(DevUi.Caption(L("{0} × {1} · {2} Hz · {3} · couleurs {4} bits · carte : {5}", d.Width, d.Height, d.RefreshHz, d.OrientationLabel, d.BitsPerPixel, d.AdapterName)));
        if (d.MaxRefreshAtCurrent > d.RefreshHz)
        {
            var hint = DevUi.Caption(L("Cet écran accepte jusqu'à {0} Hz dans cette résolution : une fréquence plus élevée rend l'affichage plus fluide (consommation un peu plus élevée sur batterie).", d.MaxRefreshAtCurrent));
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Pp.AccentText");
            hint.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(hint);
        }
        else if ((long)d.MaxWidth * d.MaxHeight > (long)d.Width * d.Height)
        {
            text.Children.Add(DevUi.Caption(L("Résolution maximale proposée : {0} × {1}.", d.MaxWidth, d.MaxHeight)));
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var metric = new TextBlock { Text = L("{0} Hz", d.RefreshHz), VerticalAlignment = VerticalAlignment.Center, FontSize = 18 }.Styled("Pp.Metric");
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
