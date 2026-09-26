using System.Windows;
using System.Windows.Controls;
using Timonier.Core.Platform;

namespace Timonier.Modules.Maintenance;

/// <summary>Section « Journal des erreurs » : groupes d'événements des 7 derniers jours, avec explications.</summary>
internal sealed class EventsPanel
{
    private const int PageSize = 25;

    public FrameworkElement Root { get; }
    public event Action<EventScan>? Scanned;

    private readonly TextBlock _summary = MaintUi.Text(L("Waiting to read the logs…"), "Pp.Body");
    private readonly ProgressBar _busyBar = MaintUi.BusyBar(100);
    private readonly Button _refresh;
    private readonly CheckBox _hideHarmless;
    private readonly StackPanel _list = new();
    private readonly Button _more;
    private EventScan? _scan;
    private int _shown = PageSize;
    private bool _busy;

    public EventsPanel()
    {
        _refresh = MaintUi.Button(L("Refresh"), "", "Pp.Button", async (_, _) => await LoadAsync());
        var viewer = MaintUi.Button(L("Event Viewer"), "", "Pp.SubtleButton",
            (_, _) => MaintUi.OpenTool(SystemTool.Mmc, Path.Combine(Environment.SystemDirectory, "eventvwr.msc")));
        viewer.Margin = new Thickness(8, 0, 0, 0);
        _hideHarmless = new CheckBox { Content = L("Hide known harmless events"), IsChecked = true, Margin = new Thickness(0, 12, 0, 0) };
        _hideHarmless.Checked += (_, _) => Render();
        _hideHarmless.Unchecked += (_, _) => Render();

        var head = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(_refresh);
        buttons.Children.Add(viewer);
        DockPanel.SetDock(buttons, Dock.Right);
        head.Children.Add(buttons);
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(_summary);
        _busyBar.Margin = new Thickness(12, 0, 0, 0);
        left.Children.Add(_busyBar);
        head.Children.Add(left);

        var top = new StackPanel();
        top.Children.Add(head);
        top.Children.Add(_hideHarmless);

        _more = MaintUi.Button(L("Show more"), "", "Pp.SubtleButton", (_, _) => { _shown += PageSize; Render(); });
        _more.HorizontalAlignment = HorizontalAlignment.Left;
        _more.Visibility = Visibility.Collapsed;
        _more.Margin = new Thickness(0, 4, 0, 0);

        var root = new StackPanel();
        root.Children.Add(MaintUi.Card(top, new Thickness(0, 0, 0, 10)));
        root.Children.Add(_list);
        root.Children.Add(_more);
        Root = root;
    }

    public async Task LoadAsync()
    {
        if (_busy) return;
        _busy = true;
        _refresh.IsEnabled = false;
        _busyBar.Visibility = Visibility.Visible;
        _summary.Text = L("Reading the System and Application logs…");
        try
        {
            _scan = await Task.Run(() => EventLogService.Load(CancellationToken.None));
            _shown = PageSize;
            Render();
            Scanned?.Invoke(_scan);
        }
        catch (Exception ex)
        {
            Log.Error("Maintenance", "journal des événements", ex);
            _summary.Text = L("Couldn't read the event logs.");
            _list.Children.Clear();
        }
        finally
        {
            _busy = false;
            _refresh.IsEnabled = true;
            _busyBar.Visibility = Visibility.Collapsed;
        }
    }

    private void Render()
    {
        _list.Children.Clear();
        if (_scan is not { } scan) return;
        var hide = _hideHarmless.IsChecked == true;
        var visible = scan.Groups.Where(g => !hide || g.Hint?.Tone != HintTone.Harmless).ToList();
        var hidden = scan.Groups.Count - visible.Count;
        var critical = scan.Groups.Where(g => g.Critical).Sum(g => g.Count);

        _summary.Text = scan.Total == 0
            ? L("No errors logged in the last 7 days.")
            : Summary(scan, critical);

        if (scan.Error is { } error)
        {
            var (bar, text, icon) = MaintUi.InfoBar("Pp.InfoBar.Warning", "");
            MaintUi.SetInfo(bar, text, icon, error, "Pp.InfoBar.Warning", "");
            bar.Margin = new Thickness(0, 0, 0, 8);
            _list.Children.Add(bar);
        }

        if (visible.Count == 0)
        {
            var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 8) };
            var i = MaintUi.Icon("", 28, "Pp.Success");
            i.HorizontalAlignment = HorizontalAlignment.Center;
            empty.Children.Add(i);
            var t = MaintUi.Text(scan.Total == 0 ? L("Nothing to report: no errors or critical events.") :
                LP(hidden, "Only known harmless events ({0} type hidden).", "Only known harmless events ({0} types hidden)."), "Pp.Body");
            t.Margin = new Thickness(0, 8, 0, 0);
            t.TextAlignment = TextAlignment.Center;
            empty.Children.Add(t);
            _list.Children.Add(MaintUi.Card(empty));
            _more.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var g in visible.Take(_shown)) _list.Children.Add(BuildRow(g));
        var remaining = visible.Count - Math.Min(_shown, visible.Count);
        _more.Visibility = remaining > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_more.Content is StackPanel sp && sp.Children.Count == 2 && sp.Children[1] is TextBlock label)
            label.Text = LP(remaining, "Show more ({0} left)", "Show more ({0} left)");
        if (hide && hidden > 0)
        {
            var note = MaintUi.Text(LP(hidden, "{0} harmless event type hidden.", "{0} harmless event types hidden."), "Pp.Caption");
            note.Margin = new Thickness(2, 6, 0, 0);
            _list.Children.Add(note);
        }
    }

    /// <summary>Résumé : « 120 événements en 7 jours, 14 types différents, dont 2 critiques (limité aux plus récents). »</summary>
    private static string Summary(EventScan scan, int critical)
    {
        var parts = new List<string>
        {
            scan.Capped ? LP(scan.Total, "{0}+ event in 7 days", "{0}+ events in 7 days")
                : LP(scan.Total, "{0} event in 7 days", "{0} events in 7 days"),
            LP(scan.Groups.Count, "{0} different type", "{0} different types"),
        };
        if (critical > 0) parts.Add(LP(critical, "including {0} critical", "including {0} critical"));
        var text = string.Join(", ", parts);
        return (scan.Capped ? L("{0} (limited to the most recent)", text) : text) + ".";
    }

    private static FrameworkElement BuildRow(EventGroup g)
    {
        var (glyph, fg, bg) = g.Critical
            ? ("", "Pp.Danger", "Pp.DangerBackground")
            : g.Hint?.Tone == HintTone.Harmless ? ("", "Pp.TextSecondary", "Pp.CardSecondary")
            : ("", "Pp.Warning", "Pp.WarningBackground");
        var tile = MaintUi.IconTile(glyph, fg, bg, 32);

        var titleRow = new WrapPanel();
        var title = MaintUi.Text($"{g.ShortProvider} · {g.EventId}", "Pp.CardTitle", wrap: false);
        title.Margin = new Thickness(0, 0, 8, 0);
        title.ToolTip = g.Provider;
        titleRow.Children.Add(title);
        titleRow.Children.Add(MaintUi.Badge(LP(g.Count, "{0} time", "{0} times"), g.Count >= 10 ? "Pp.Warning" : "Pp.TextSecondary", g.Count >= 10 ? "Pp.WarningBackground" : "Pp.CardSecondary"));
        var logBadge = MaintUi.Badge(g.Log == "System" ? LC("journal", "System") : LC("journal", "Application"));
        logBadge.Margin = new Thickness(6, 0, 0, 0);
        titleRow.Children.Add(logBadge);
        if (g.Critical)
        {
            var c = MaintUi.Badge(L("Critical"), "Pp.Danger", "Pp.DangerBackground");
            c.Margin = new Thickness(6, 0, 0, 0);
            titleRow.Children.Add(c);
        }

        var texts = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        texts.Children.Add(titleRow);
        var last = MaintUi.Text(g.Last == DateTime.MinValue ? L("Last occurrence: unknown")
            : L("Last occurrence: {0} ({1})", Format.Date(g.Last), Format.Ago(g.Last)), "Pp.Caption");
        last.Margin = new Thickness(0, 3, 0, 0);
        texts.Children.Add(last);
        var message = MaintUi.Text(g.FirstLine, "Pp.Body");
        message.Margin = new Thickness(0, 6, 0, 0);
        message.MaxHeight = 44;
        message.TextTrimming = TextTrimming.CharacterEllipsis;
        message.ToolTip = g.FirstLine;
        texts.Children.Add(message);
        if (g.Hint is { } hint)
        {
            var hintRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var hi = MaintUi.Icon(hint.Tone == HintTone.Harmless ? "" : "", 13,
                hint.Tone switch { HintTone.Harmless => "Pp.Success", HintTone.Attention => "Pp.Warning", _ => "Pp.AccentText" });
            hi.VerticalAlignment = VerticalAlignment.Top;
            hi.Margin = new Thickness(0, 2, 8, 0);
            DockPanel.SetDock(hi, Dock.Left);
            hintRow.Children.Add(hi);
            hintRow.Children.Add(MaintUi.Text(hint.Text, "Pp.Caption"));
            texts.Children.Add(hintRow);
        }

        var dock = new DockPanel();
        DockPanel.SetDock(tile, Dock.Left);
        dock.Children.Add(tile);
        dock.Children.Add(texts);
        return MaintUi.Card(dock, new Thickness(0, 0, 0, 8));
    }
}
