using System.Text;
using System.Windows;
using System.Windows.Controls;
using PcPilot.Core.Engine;
using PcPilot.Core.Platform;
using PcPilot.UI.Services;

namespace PcPilot.Modules.Apps;

/// <summary>
/// Carte d'activité de la page : une seule opération winget/Appx à la fois, progression en direct (dernière ligne +
/// journal détaillé repliable), bouton Annuler, puis bilan par application. Garde PC Pilot actif si la fenêtre est fermée.
/// </summary>
internal sealed class ActivityPanel : ContentControl
{
    private readonly TextBlock _title = AppsUi.Strong("", 14);
    private readonly TextBlock _line = AppsUi.Caption("");
    private readonly ProgressBar _bar = new() { IsIndeterminate = true, Height = 3, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Border _statusIcon;
    private readonly TextBlock _statusGlyph = AppsUi.Icon("", 16);
    private readonly TextBox _log;
    private readonly Expander _details;
    private readonly StackPanel _results = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _cancel;
    private readonly Button _close;
    private readonly StringBuilder _logText = new();
    private int _logLines;
    private CancellationTokenSource? _cts;

    public bool IsBusy { get; private set; }
    public event Action? BusyChanged;

    public ActivityPanel()
    {
        Focusable = false;
        Visibility = Visibility.Collapsed;
        Margin = new Thickness(0, 12, 0, 0);

        _statusIcon = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Child = _statusGlyph, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        _statusGlyph.HorizontalAlignment = HorizontalAlignment.Center;

        _cancel = AppsUi.Button("Annuler", "", "Pp.Button", (s, _) => { _cts?.Cancel(); ((Button)s).IsEnabled = false; _line.Text = "Annulation…"; });
        _close = AppsUi.Button("Fermer", null, "Pp.SubtleButton", (_, _) => Visibility = Visibility.Collapsed);
        var buttons = AppsUi.Row(_cancel, _close);
        buttons.VerticalAlignment = VerticalAlignment.Top;

        _log = new TextBox
        {
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 220, FontSize = 12,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"),
        };
        _details = new Expander { Header = "Détails de l'opération", Content = _log, Margin = new Thickness(0, 8, 0, 0), IsExpanded = false };
        _details.Expanded += (_, _) => { _log.Text = _logText.ToString(); _log.ScrollToEnd(); };

        var text = new StackPanel();
        _line.TextTrimming = TextTrimming.CharacterEllipsis;
        _line.TextWrapping = TextWrapping.NoWrap;
        _line.Margin = new Thickness(0, 3, 0, 0);
        text.Children.Add(_title);
        text.Children.Add(_line);

        var head = new DockPanel();
        DockPanel.SetDock(_statusIcon, Dock.Left);
        DockPanel.SetDock(buttons, Dock.Right);
        head.Children.Add(_statusIcon);
        head.Children.Add(buttons);
        head.Children.Add(text);

        var body = new StackPanel();
        body.Children.Add(head);
        body.Children.Add(_bar);
        body.Children.Add(_results);
        body.Children.Add(_details);
        Content = AppsUi.Card(body, new Thickness(16, 14, 16, 12));
    }

    /// <summary>Lance une action (locale ou via le broker) en affichant sa progression ; null si une opération est déjà en cours.</summary>
    public async Task<ApplyOutcome?> RunAsync(string title, string actionId, Dictionary<string, string> parameters)
    {
        if (IsBusy) { AppHost.Toasts.Show("Une opération est déjà en cours : patientez ou annulez-la.", ToastKind.Warning); return null; }
        SetBusy(true);
        _cts = new CancellationTokenSource();
        _title.Text = title;
        _line.Text = "Préparation…";
        _results.Children.Clear();
        _results.Visibility = Visibility.Collapsed;
        _logText.Clear();
        _logLines = 0;
        _log.Text = "";
        _bar.Visibility = Visibility.Visible;
        _cancel.Visibility = Visibility.Visible;
        _cancel.IsEnabled = true;
        _close.Visibility = Visibility.Collapsed;
        SetStatus("", "Pp.AccentSubtle", "Pp.AccentText");
        Visibility = Visibility.Visible;
        BringIntoView();

        var progress = new Progress<string>(OnLine);
        using var keepAlive = AppHost.Background.Acquire(title);
        ApplyOutcome outcome;
        try
        {
            outcome = await AppHost.Engine.RunActionAsync(actionId, parameters, progress, _cts.Token);
        }
        catch (Exception ex)
        {
            Log.Error("Apps", "opération " + actionId, ex);
            outcome = new ApplyOutcome(false, ex.Message);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
        }

        _bar.Visibility = Visibility.Collapsed;
        _cancel.Visibility = Visibility.Collapsed;
        _close.Visibility = Visibility.Visible;
        _line.Text = outcome.Message;
        if (outcome.Cancelled) SetStatus("", "Pp.NeutralBackground", "Pp.Neutral");
        else if (outcome.Success) SetStatus("", "Pp.SuccessBackground", "Pp.Success");
        else SetStatus("", "Pp.WarningBackground", "Pp.Warning");
        ShowResults(outcome);
        SetBusy(false);
        AppHost.Toasts.ShowOutcome(outcome);
        return outcome;
    }

    private void ShowResults(ApplyOutcome outcome)
    {
        if (outcome.Data is not { Count: > 0 } data) return;
        var rows = data.Where(kv => kv.Value.StartsWith("ok : ", StringComparison.Ordinal) || kv.Value.StartsWith("erreur : ", StringComparison.Ordinal)).ToList();
        if (rows.Count < 2 && outcome.Success) return;
        foreach (var (id, value) in rows)
        {
            var ok = value.StartsWith("ok", StringComparison.Ordinal);
            var icon = AppsUi.Icon(ok ? "" : "", 13, ok ? "Pp.Success" : "Pp.Warning");
            icon.Margin = new Thickness(0, 0, 8, 0);
            var name = AppsUi.Text(AppsCatalog.Find(id)?.Name ?? id);
            name.FontSize = 13;
            name.TextWrapping = TextWrapping.NoWrap;
            var detail = AppsUi.Caption(" — " + value[(value.IndexOf(':') + 2)..]);
            detail.TextWrapping = TextWrapping.NoWrap;
            detail.VerticalAlignment = VerticalAlignment.Center;
            var row = AppsUi.Row(icon, name, detail);
            row.Margin = new Thickness(40, 2, 0, 2);
            _results.Children.Add(row);
        }
        _results.Visibility = _results.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnLine(string line)
    {
        _line.Text = line;
        _logText.AppendLine(line);
        if (++_logLines > 400)
        {
            // Borne la mémoire : on garde les 300 dernières lignes.
            var all = _logText.ToString().Split(Environment.NewLine);
            _logText.Clear();
            foreach (var l in all.TakeLast(300)) if (l.Length > 0) _logText.AppendLine(l);
            _logLines = 300;
        }
        if (_details.IsExpanded)
        {
            _log.Text = _logText.ToString();
            _log.ScrollToEnd();
        }
    }

    private void SetStatus(string glyph, string background, string foreground)
    {
        _statusGlyph.Text = glyph;
        _statusGlyph.SetResourceReference(TextBlock.ForegroundProperty, foreground);
        _statusIcon.SetResourceReference(Border.BackgroundProperty, background);
    }

    private void SetBusy(bool busy)
    {
        IsBusy = busy;
        BusyChanged?.Invoke();
    }
}
