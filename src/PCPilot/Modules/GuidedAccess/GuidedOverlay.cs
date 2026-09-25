using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace PcPilot.Modules.GuidedAccess;

internal enum OverlayMode
{
    /// <summary>Geste de sortie : saisir le code pour terminer, ou reprendre.</summary>
    Exit,
    /// <summary>Limite de temps atteinte : code requis pour terminer ou prolonger.</summary>
    TimeUp,
    /// <summary>L'application cible s'est fermée : relancer (si lancée par PC Pilot) ou code pour terminer.</summary>
    TargetClosed,
}

internal enum OverlayChoice { Resume, End, Extend, Relaunch }

/// <summary>
/// Écran plein écran, au premier plan, appartenant à PC Pilot : demande le code de sortie. Il ne se ferme pas avec Alt+F4 ;
/// les essais erronés sont limités en fréquence (<see cref="GuidedPin"/>).
/// </summary>
internal sealed class GuidedOverlay : Window
{
    public const int ExtendMinutes = 15;

    private readonly OverlayMode _mode;
    private readonly PasswordBox _pin = new() { MaxLength = GuidedPin.MaxLength, Width = 260, FontSize = 18, HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 10, 0, 0), MinHeight = 18 };
    private readonly Button _end;
    private readonly Button? _extend;
    private readonly DispatcherTimer _lockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _busy;
    private bool _allowClose;

    public event EventHandler<OverlayChoice>? Chosen;

    public GuidedOverlay(OverlayMode mode, string appTitle, bool canRelaunch)
    {
        _mode = mode;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Title = "Accès guidé — PC Pilot";
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        SetResourceReference(BackgroundProperty, "Pp.WindowBackground");
        SetResourceReference(ForegroundProperty, "Pp.TextPrimary");
        UseLayoutRounding = true;

        var (glyph, title, message) = mode switch
        {
            OverlayMode.TimeUp => ("", "Temps écoulé",
                $"Le temps prévu pour « {appTitle} » est terminé. Saisissez le code pour quitter l'accès guidé ou prolonger de {ExtendMinutes} minutes."),
            OverlayMode.TargetClosed => ("", "L'application s'est fermée",
                canRelaunch
                    ? $"« {appTitle} » n'est plus ouverte. Vous pouvez la relancer, ou saisir le code pour quitter l'accès guidé."
                    : $"« {appTitle} » n'est plus ouverte. Saisissez le code pour quitter l'accès guidé."),
            _ => ("", "Quitter l'accès guidé ?",
                $"Saisissez le code pour déverrouiller le PC. Choisissez « Reprendre » pour revenir à « {appTitle} »."),
        };

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 440 };

        var icon = new TextBlock { Text = glyph, FontSize = 30, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(StyleProperty, "Pp.Icon");
        icon.SetResourceReference(TextBlock.ForegroundProperty, mode == OverlayMode.TargetClosed ? "Pp.Warning" : "Pp.AccentText");
        var iconBox = new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(36), Child = icon, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 18) };
        iconBox.SetResourceReference(Border.BackgroundProperty, mode == OverlayMode.TargetClosed ? "Pp.WarningBackground" : "Pp.AccentSubtle");
        stack.Children.Add(iconBox);

        var titleBlock = new TextBlock { Text = title, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
        titleBlock.SetResourceReference(StyleProperty, "Pp.PageTitle");
        stack.Children.Add(titleBlock);
        var body = new TextBlock { Text = message, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 8, 0, 22) };
        body.SetResourceReference(StyleProperty, "Pp.Body");
        body.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(body);

        var label = new TextBlock { Text = "Code de sortie", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
        label.SetResourceReference(StyleProperty, "Pp.Caption");
        stack.Children.Add(label);
        _pin.HorizontalAlignment = HorizontalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(_pin, "Code de sortie");
        _pin.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; _ = SubmitAsync(OverlayChoice.End); }
        };
        _pin.PasswordChanged += (_, _) => { if (!_busy && GuidedPin.LockRemaining == TimeSpan.Zero) _error.Text = ""; };
        stack.Children.Add(_pin);
        _error.SetResourceReference(TextBlock.ForegroundProperty, "Pp.Danger");
        _error.FontSize = 12;
        stack.Children.Add(_error);

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        _end = MakeButton("Terminer l'accès guidé", "Pp.AccentButton", (_, _) => _ = SubmitAsync(OverlayChoice.End));
        _end.IsDefault = false;
        buttons.Children.Add(_end);
        if (mode == OverlayMode.TimeUp)
        {
            _extend = MakeButton($"Prolonger de {ExtendMinutes} min", "Pp.Button", (_, _) => _ = SubmitAsync(OverlayChoice.Extend));
            buttons.Children.Add(_extend);
        }
        if (mode == OverlayMode.TargetClosed && canRelaunch)
            buttons.Children.Add(MakeButton("Relancer l'application", "Pp.Button", (_, _) => Finish(OverlayChoice.Relaunch)));
        if (mode == OverlayMode.Exit)
            buttons.Children.Add(MakeButton("Reprendre", "Pp.Button", (_, _) => Finish(OverlayChoice.Resume)));
        stack.Children.Add(buttons);

        var hint = new TextBlock
        {
            Text = "Accès guidé · PC Pilot",
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 28),
        };
        hint.SetResourceReference(StyleProperty, "Pp.Caption");
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Pp.TextTertiary");

        var root = new Grid();
        root.Children.Add(stack);
        root.Children.Add(hint);
        Content = root;

        _lockTimer.Tick += (_, _) => UpdateLockState();
        Loaded += (_, _) =>
        {
            UpdateLockState();
            _pin.Focus();
        };
        Closed += (_, _) => _lockTimer.Stop();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _mode == OverlayMode.Exit) { e.Handled = true; Finish(OverlayChoice.Resume); }
        };
    }

    public nint Handle => new WindowInteropHelper(this).Handle;

    /// <summary>Ferme l'écran (appelé par la session uniquement).</summary>
    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose) e.Cancel = true;   // Alt+F4 ou fermeture externe : ignorées
        base.OnClosing(e);
    }

    private static Button MakeButton(string text, string style, RoutedEventHandler click)
    {
        var b = new Button { Content = text, Margin = new Thickness(4), MinWidth = 150 };
        b.SetResourceReference(StyleProperty, style);
        b.Click += click;
        return b;
    }

    private void Finish(OverlayChoice choice)
    {
        if (_busy) return;
        Chosen?.Invoke(this, choice);
    }

    private async Task SubmitAsync(OverlayChoice choice)
    {
        if (_busy) return;
        if (GuidedPin.LockRemaining > TimeSpan.Zero) { UpdateLockState(); return; }
        var pin = _pin.Password;
        if (pin.Length == 0)
        {
            _error.Text = "Saisissez le code de sortie.";
            _pin.Focus();
            return;
        }
        SetBusy(true);
        bool ok;
        try { ok = await GuidedPin.VerifyAsync(pin); }
        finally { SetBusy(false); }
        _pin.Clear();
        if (ok)
        {
            Chosen?.Invoke(this, choice);
            return;
        }
        _error.Text = GuidedPin.Failures >= 3 ? "" : "Code incorrect.";
        UpdateLockState();
        _pin.Focus();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _pin.IsEnabled = !busy && GuidedPin.LockRemaining == TimeSpan.Zero;
        _end.IsEnabled = _pin.IsEnabled;
        if (_extend is not null) _extend.IsEnabled = _pin.IsEnabled;
        Cursor = busy ? Cursors.Wait : null;
    }

    private void UpdateLockState()
    {
        var left = GuidedPin.LockRemaining;
        if (left > TimeSpan.Zero)
        {
            _error.Text = $"Trop d'essais incorrects. Réessayez dans {(int)Math.Ceiling(left.TotalSeconds)} s.";
            if (!_lockTimer.IsEnabled) _lockTimer.Start();
        }
        else
        {
            if (_lockTimer.IsEnabled)
            {
                _lockTimer.Stop();
                _error.Text = "";
            }
        }
        SetBusy(_busy);
        if (_pin.IsEnabled && IsActive) _pin.Focus();
    }
}
