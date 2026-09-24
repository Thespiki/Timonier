using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using PcPilot.Core.Platform;
using PcPilot.UI.Controls;
using PcPilot.UI.Services;

namespace PcPilot.Modules.Apps;

/// <summary>
/// Page « Applications » : sélecteur segmenté vers quatre vues chargées à la demande (Installer, Installées,
/// Préinstallées, Mises à jour), carte d'activité commune et barre d'action fixe pour l'installation groupée.
/// Paramètres de navigation : « search:&lt;texte&gt; », « view:install|installed|bloat|updates », « action:update-all ».
/// </summary>
public sealed class AppsPage : UserControl, INavigationAware
{
    public enum View { Install, Installed, Bloat, Updates }

    private readonly ScrollViewer _scroll;
    private readonly ContentControl _viewHost = new() { Focusable = false };
    private readonly Dictionary<View, RadioButton> _segments = [];
    private readonly AppsContext _ctx;
    private readonly InstallView _install;
    private InstalledView? _installed;
    private BloatView? _bloat;
    private UpdatesView? _updates;
    private readonly Border _bottomBar;
    private View _current = View.Install;
    private string? _pending;
    private bool _firstLoad = true;

    public AppsPage()
    {
        Focusable = false;
        var activity = new ActivityPanel();
        _ctx = new AppsContext(activity) { WingetAvailable = Winget.IsAvailable };

        var stack = new StackPanel().Styled("Pp.PageStack");
        _scroll = new ScrollViewer { Content = stack }.Styled("Pp.PageScroll");

        stack.Children.Add(new PageHeader
        {
            Title = "Applications",
            Subtitle = "Installez des logiciels vérifiés en un clic, désinstallez des programmes, supprimez les applications préinstallées " +
                       "superflues et gardez tout à jour avec winget.",
            Glyph = AppsModule.Glyph,
        });

        if (!_ctx.WingetAvailable)
        {
            var store = AppsUi.Button("Installer depuis le Microsoft Store", "", "Pp.Button", (_, _) =>
            {
                try { ProcessRunner.OpenSettingsUri(Winget.StoreProductUri); }
                catch (Exception ex) { AppHost.Toasts.Show("Impossible d'ouvrir le Microsoft Store : " + ex.Message, ToastKind.Error); }
            });
            stack.Children.Add(AppsUi.InfoBar(
                "winget (« Programme d'installation d'application ») est introuvable : l'installation, la désinstallation et les mises à jour " +
                "sont indisponibles. Installez-le gratuitement depuis le Microsoft Store, puis rouvrez PC Pilot.", "", "Pp.InfoBar.Warning", store));
        }

        stack.Children.Add(BuildSelector());
        stack.Children.Add(activity);
        _viewHost.Margin = new Thickness(0, 16, 0, 0);
        stack.Children.Add(_viewHost);

        _install = new InstallView(_ctx);
        _bottomBar = new Border { Focusable = false };
        _install.ActionBarChanged += UpdateBottomBar;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(_scroll);
        Grid.SetRow(_bottomBar, 1);
        root.Children.Add(_bottomBar);
        Content = root;

        Loaded += OnLoaded;
    }

    private Border BuildSelector()
    {
        var grid = new UniformGrid { Columns = 4, Rows = 1 };
        AddSegment(grid, View.Install, "", "Installer");
        AddSegment(grid, View.Installed, "", "Installées");
        AddSegment(grid, View.Bloat, "", "Préinstallées");
        AddSegment(grid, View.Updates, "", "Mises à jour");
        _segments[View.Install].IsChecked = true;
        return new Border { Child = grid, CornerRadius = new CornerRadius(8), Padding = new Thickness(2), BorderThickness = new Thickness(1) }
            .Themed(Border.BackgroundProperty, "Pp.CardSecondary")
            .Themed(Border.BorderBrushProperty, "Pp.CardBorder");
    }

    private void AddSegment(UniformGrid grid, View view, string glyph, string label)
    {
        var icon = AppsUi.InheritingIcon(glyph, 14, typeof(RadioButton));
        icon.Margin = new Thickness(0, 0, 8, 0);
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        var rb = new RadioButton { Content = AppsUi.Row(icon, text), GroupName = "apps-views-" + GetHashCode(), Style = AppsUi.SegmentStyle };
        System.Windows.Automation.AutomationProperties.SetName(rb, label);
        rb.Checked += (_, _) =>
        {
            if (_firstLoad) { _current = view; return; } // la vue initiale est affichée au premier chargement
            if (_current != view || _viewHost.Content is null) _ = ShowAsync(view);
        };
        _segments[view] = rb;
        grid.Children.Add(rb);
    }

    // ================================================================== Cycle de vie et navigation

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_firstLoad) return;
        _firstLoad = false;
        // Laisse la page s'afficher avant de construire la vue (catalogue de ~120 tuiles).
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        if (_pending is not null) HandleNavigation();
        if (_viewHost.Content is null) await ShowAsync(_current);
    }

    public void OnNavigatedTo(object? parameter)
    {
        if (parameter is not string s || s.Length == 0) return;
        _pending = s;
        if (!_firstLoad) Dispatcher.InvokeAsync(HandleNavigation, DispatcherPriority.Loaded);
    }

    private void HandleNavigation()
    {
        var p = _pending;
        _pending = null;
        if (p is null) return;
        if (p.StartsWith("search:", StringComparison.OrdinalIgnoreCase))
        {
            var text = p["search:".Length..].Trim();
            if (text.Length > 100) text = text[..100];
            Select(View.Install);
            _ = ShowThen(View.Install, () => _install.SetSearch(text));
        }
        else if (p.StartsWith("view:", StringComparison.OrdinalIgnoreCase))
        {
            var view = p["view:".Length..].ToLowerInvariant() switch
            {
                "installed" => View.Installed,
                "bloat" => View.Bloat,
                "updates" => View.Updates,
                _ => View.Install,
            };
            Select(view);
        }
        else if (p.Equals("action:update-all", StringComparison.OrdinalIgnoreCase))
        {
            Select(View.Updates);
            _ = ShowThen(View.Updates, () => _ = _updates!.UpgradeAllAsync());
        }
        else if (_viewHost.Content is null) _ = ShowAsync(View.Install);
    }

    private async Task ShowThen(View view, Action then)
    {
        await ShowAsync(view);
        then();
    }

    private void Select(View view)
    {
        if (_segments[view].IsChecked == true) { if (_viewHost.Content is null) _ = ShowAsync(view); }
        else _segments[view].IsChecked = true;
    }

    private async Task ShowAsync(View view)
    {
        _current = view;
        try
        {
            switch (view)
            {
                case View.Install:
                    _viewHost.Content = _install;
                    UpdateBottomBar();
                    await _install.ActivateAsync();
                    break;
                case View.Installed:
                    _installed ??= new InstalledView(_ctx);
                    _viewHost.Content = _installed;
                    UpdateBottomBar();
                    await _installed.ActivateAsync();
                    break;
                case View.Bloat:
                    _bloat ??= new BloatView(_ctx);
                    _viewHost.Content = _bloat;
                    UpdateBottomBar();
                    await _bloat.ActivateAsync();
                    break;
                case View.Updates:
                    _updates ??= new UpdatesView(_ctx);
                    _viewHost.Content = _updates;
                    UpdateBottomBar();
                    await _updates.ActivateAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Apps", "affichage de la vue " + view, ex);
            _viewHost.Content = AppsUi.EmptyState("", "Cette vue n'a pas pu s'afficher", ex.Message, "Pp.Warning");
        }
    }

    private void UpdateBottomBar()
    {
        _bottomBar.Child = _current == View.Install && _install.ActionBar.Visibility == Visibility.Visible ? _install.ActionBar : null;
    }
}
