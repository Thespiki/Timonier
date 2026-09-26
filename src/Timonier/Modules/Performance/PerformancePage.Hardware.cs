using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Timonier.Core.Platform;
using Timonier.UI.Services;

namespace Timonier.Modules.Performance;

public sealed partial class PerformancePage
{
    // Carte « Profil matériel »
    private readonly TextBlock _tierLabel = PerfUi.Text("Pp.Metric");
    private readonly TextBlock _tierSummary = PerfUi.Text("Pp.Caption", "", new Thickness(0, 10, 0, 0));
    private readonly Border[] _tierBars = [new(), new(), new()];
    private readonly StackPanel _factsPanel = new();
    private readonly StackPanel _tipsPanel = new();
    private PowerSource? _lastSource;

    // Carte « Options graphiques »
    private readonly Dictionary<string, ComboBox> _dxCombos = new(StringComparer.Ordinal);
    private bool _updatingGraphics;

    private Border BuildHardwareCard()
    {
        // Colonne gauche : niveau de performance
        var left = new StackPanel { Margin = new Thickness(0, 4, 24, 0) };
        left.Children.Add(new Border { Child = PerfUi.IconCircle(PerformanceModule.Glyph, 52, 22), HorizontalAlignment = HorizontalAlignment.Left });
        _tierLabel.Margin = new Thickness(0, 12, 0, 0);
        left.Children.Add(_tierLabel);
        left.Children.Add(PerfUi.Text("Pp.Caption", L("Estimated performance level")));
        var gauge = new UniformGrid { Rows = 1, Columns = 3, Height = 6, Margin = new Thickness(0, 10, 0, 0), Width = 150, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var bar in _tierBars)
        {
            bar.CornerRadius = new CornerRadius(3);
            bar.Margin = new Thickness(0, 0, 4, 0);
            gauge.Children.Add(bar);
        }
        left.Children.Add(gauge);
        left.Children.Add(_tierSummary);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(left);
        Grid.SetColumn(_factsPanel, 1);
        grid.Children.Add(_factsPanel);

        var content = new StackPanel();
        content.Children.Add(grid);
        content.Children.Add(PerfUi.Divider(14, 12));
        var tipsTitle = PerfUi.Text("Pp.CardTitle", L("What Timonier recommends for this hardware"));
        tipsTitle.FontWeight = FontWeights.SemiBold;
        tipsTitle.Margin = new Thickness(0, 0, 0, 6);
        content.Children.Add(tipsTitle);
        content.Children.Add(_tipsPanel);

        RefreshHardware();
        return PerfUi.Card(content, new Thickness(0, 0, 0, 4)).WithPadding(new Thickness(20, 16, 20, 16));
    }

    private void RefreshHardware()
    {
        var p = AppHost.Profile;
        _tierLabel.Text = p.Tier == PerformanceTier.Unknown ? LC("short (tile)", "Analyzing…") : p.TierLabel;
        _tierSummary.Text = HardwareAdvice.TierSummary(p);
        var level = p.Tier switch { PerformanceTier.Low => 1, PerformanceTier.Medium => 2, PerformanceTier.High => 3, _ => 0 };
        for (var i = 0; i < _tierBars.Length; i++)
            _tierBars[i].SetResourceReference(Border.BackgroundProperty, i < level ? "Pp.Accent" : "Pp.ControlStroke");

        _factsPanel.Children.Clear();
        if (!p.HardwareLoaded && string.IsNullOrEmpty(p.CpuName))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            row.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 120, Height = 3, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(PerfUi.Text("Pp.Caption", LC("long form", "Scanning hardware…"), new Thickness(12, 0, 0, 0)));
            _factsPanel.Children.Add(row);
        }
        else
        {
            foreach (var fact in HardwareAdvice.Facts(p, _lastSource)) _factsPanel.Children.Add(BuildFactRow(fact));
        }

        _tipsPanel.Children.Clear();
        var tips = HardwareAdvice.Tips(p);
        if (tips.Count == 0)
            _tipsPanel.Children.Add(PerfUi.Text("Pp.Caption", L("Tips will appear as soon as the hardware has been analyzed.")));
        foreach (var tip in tips) _tipsPanel.Children.Add(BuildTipRow(tip));
    }

    private static Grid BuildFactRow(HardwareFact fact)
    {
        var g = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = PerfUi.Icon(fact.Glyph, 16, "Pp.TextSecondary");
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 2, 0, 0);
        var label = PerfUi.Text("Pp.Caption", fact.Label, new Thickness(0, 2, 8, 0));
        var value = PerfUi.Text("Pp.Body", fact.Value, new Thickness(0, 0, 12, 0));
        Grid.SetColumn(label, 1);
        Grid.SetColumn(value, 2);
        g.Children.Add(icon);
        g.Children.Add(label);
        g.Children.Add(value);
        if (fact.Verdict is { } verdict)
        {
            var badge = PerfUi.Badge(verdict, fact.Tone);
            badge.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(badge, 3);
            g.Children.Add(badge);
        }
        return g;
    }

    private DockPanel BuildTipRow(HardwareTip tip)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 5, 0, 5), LastChildFill = true };
        var icon = PerfUi.Icon(tip.Glyph, 14, "Pp.AccentText");
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 3, 12, 0);
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);
        if (tip.TweakId is { } id)
        {
            var link = new Button { Content = L("View setting"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 0, 0) };
            link.SetResourceReference(StyleProperty, "Pp.LinkButton");
            link.Click += (_, _) => OnNavigatedTo("tweak:" + id);
            DockPanel.SetDock(link, Dock.Right);
            dock.Children.Add(link);
        }
        dock.Children.Add(PerfUi.Text("Pp.Body", tip.Text));
        return dock;
    }

    // ================================================================== Options graphiques (DirectX)

    private Border BuildGraphicsCard()
    {
        var content = new StackPanel();

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var circle = PerfUi.IconCircle("", 36, 16);
        circle.Margin = new Thickness(0, 0, 12, 0);
        circle.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(circle, Dock.Left);
        header.Children.Add(circle);
        var titles = new StackPanel();
        var title = PerfUi.Text("Pp.CardTitle", L("Windows graphics options for games"));
        title.FontWeight = FontWeights.SemiBold;
        titles.Children.Add(title);
        titles.Children.Add(PerfUi.Text("Pp.Caption",
            L("Settings › System › Display › Graphics. Settings for your account, taking effect the next time games are launched.")));
        header.Children.Add(titles);
        content.Children.Add(header);

        var build = AppHost.Profile.Build;
        content.Children.Add(PerfUi.Divider(12, 4));
        content.Children.Add(BuildDxRow("SwapEffectUpgradeEnable",
            L("Optimizations for windowed games"),
            L("Switches DirectX 10 and 11 games running windowed or borderless windowed to the modern presentation model: lower latency, and Auto HDR or variable refresh rate become possible. No effect on DirectX 12 games or games in exclusive full screen."),
            build >= 22000 ? null : L("Requires Windows 11.")));
        content.Children.Add(PerfUi.Divider(4, 4));
        content.Children.Add(BuildDxRow("VRROptimizeEnable",
            L("Optimizations for variable refresh rate"),
            L("Lets variable refresh rate (G-SYNC Compatible, FreeSync, Adaptive-Sync) work with DirectX 11 games that don't support it themselves. No effect if the display isn't VRR-compatible."),
            build >= 18362 ? null : LC("feminine", "Requires Windows 10 version 1903 or later.")));

        return PerfUi.Card(content, new Thickness(0, 8, 0, 4)).WithPadding(new Thickness(20, 16, 20, 12));
    }

    private Grid BuildDxRow(string setting, string title, string description, string? unavailable)
    {
        var g = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var texts = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };
        texts.Children.Add(PerfUi.Text("Pp.Body", title));
        texts.Children.Add(PerfUi.Text("Pp.Caption", unavailable ?? description, new Thickness(0, 2, 0, 0)));
        g.Children.Add(texts);

        var combo = new ComboBox { Width = 200, VerticalAlignment = VerticalAlignment.Center, IsEnabled = unavailable is null };
        combo.Items.Add(new ComboBoxItem { Content = L("Windows default"), Tag = "default" });
        combo.Items.Add(new ComboBoxItem { Content = L("On"), Tag = "1" });
        combo.Items.Add(new ComboBoxItem { Content = L("Off"), Tag = "0" });
        System.Windows.Automation.AutomationProperties.SetName(combo, title);
        combo.SelectionChanged += async (_, _) =>
        {
            if (_updatingGraphics || combo.SelectedItem is not ComboBoxItem { Tag: string value }) return;
            combo.IsEnabled = false;
            try
            {
                var outcome = await AppHost.Engine.RunActionAsync(DirectXSettingAction.ActionId,
                    new Dictionary<string, string> { ["setting"] = setting, ["value"] = value });
                AppHost.Toasts.ShowOutcome(outcome);
            }
            finally
            {
                combo.IsEnabled = true;
                await RefreshGraphicsAsync();
            }
        };
        Grid.SetColumn(combo, 1);
        g.Children.Add(combo);
        _dxCombos[setting] = combo;
        return g;
    }

    private async Task RefreshGraphicsAsync()
    {
        if (_dxCombos.Count == 0) return; // carte pas encore construite (créée avec la liste « Jeux »)
        try
        {
            var values = await Task.Run(() => _dxCombos.Keys.ToDictionary(k => k, DirectXSettingAction.Current));
            _updatingGraphics = true;
            foreach (var (setting, combo) in _dxCombos)
            {
                var key = values[setting] switch { "1" => "1", "0" => "0", _ => "default" };
                combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == key);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Performance", "lecture DirectX : " + ex.Message);
        }
        finally { _updatingGraphics = false; }
    }
}

internal static class BorderExtensions
{
    public static Border WithPadding(this Border b, Thickness padding)
    {
        b.Padding = padding;
        return b;
    }
}
