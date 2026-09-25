using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Timonier.Core.Catalog;
using Timonier.Core.Engine;
using Timonier.Core.Model;
using Timonier.Core.Platform;
using Timonier.UI.Services;

namespace Timonier.Modules.Dashboard;

/// <summary>
/// Page d'accueil : identité du PC, tuiles en direct (minuteur actif uniquement quand la page est visible), santé,
/// recommandations, actions rapides, outils du fabricant et transparence. Tout le travail lent est fait hors du thread UI.
/// Le code ne dépend que des API du registre : les sections s'adaptent aux modules réellement présents.
/// </summary>
public sealed class DashboardPage : UserControl, INavigationAware
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);
    private const int QuickPreview = 8;

    private readonly ScrollViewer _scroll;
    private readonly StackPanel _stack;
    private readonly Dictionary<string, FrameworkElement> _anchors = new(StringComparer.Ordinal);
    private string? _pendingSection;

    // Héros
    private readonly TextBlock _greeting = new();
    private readonly TextBlock _date = new();
    private bool _hardwareApplied;
    private readonly TextBlock _heroStatusText = new();
    private readonly TextBlock _heroStatusIcon = new();
    private readonly Border _heroStatus = new();
    private readonly TextBlock _pcName = new();
    private readonly TextBlock _pcModel = new();
    private readonly WrapPanel _pcChips = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly Border _pcIcon;
    private readonly ContentControl _specsHost = new() { Focusable = false };

    // En direct
    private readonly LiveMetrics _metrics = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly ContentControl _liveHost = new() { Focusable = false };
    private LiveTile? _cpuTile, _ramTile, _diskTile, _batteryTile, _netTile, _uptimeTile;
    private bool? _liveHasBattery;
    private bool _sampling;
    private Window? _window;

    // Santé
    private readonly TextBlock _healthHeadline = new();
    private readonly TextBlock _healthSummary = new();
    private readonly TextBlock _healthUpdated = new();
    private readonly ContentControl _healthIcon = new() { Focusable = false };
    private readonly ProgressBar _healthProgress = new() { IsIndeterminate = true, Height = 3, Margin = new Thickness(0, 10, 0, 0) };
    private readonly ContentControl _healthList = new() { Focusable = false };
    private readonly Button _healthRefresh;
    private List<(IHealthCheck Check, HealthResult Result)>? _health;
    private DateTime _healthAt;
    private bool _healthBusy, _healthStale, _healthShowGood;

    // Recommandations
    private readonly ContentControl _recoHost = new() { Focusable = false };
    private DateTime _recoAt;
    private bool _recoBusy, _recoStale, _recoDone;

    // Actions rapides
    private readonly ContentControl _quickHost = new() { Focusable = false };
    private readonly Button _quickToggle;
    private bool _quickAll;

    // Outils du fabricant
    private readonly StackPanel _vendorSection = new() { Visibility = Visibility.Collapsed };

    // Transparence
    private readonly ContentControl _transparencyHost = new() { Focusable = false };

    private readonly DispatcherTimer _changedDebounce = new() { Interval = TimeSpan.FromMilliseconds(1500) };

    public DashboardPage()
    {
        Focusable = false;
        _stack = new StackPanel();
        _stack.SetResourceReference(StyleProperty, "Pp.PageStack");
        _scroll = new ScrollViewer { Content = _stack };
        _scroll.SetResourceReference(StyleProperty, "Pp.PageScroll");
        Content = _scroll;

        _pcIcon = DashUi.IconBox("", 56, 26, radius: 12);
        _stack.Children.Add(BuildHero());

        // En direct
        var liveCaption = DashUi.Text("Actualisé toutes les 2 secondes, uniquement quand cette page est affichée.", "Pp.Caption");
        AddSection("live", "En direct", liveCaption, null);
        _stack.Children.Add(_liveHost);
        BuildLiveTiles(DashNative.GetSystemPowerStatus(out var power) && power.BatteryFlag is not (128 or 255));

        // Santé
        _healthRefresh = DashUi.Button("Actualiser", "", "Pp.SubtleButton");
        _healthRefresh.Click += async (_, _) => await RefreshHealthAsync();
        _healthUpdated.SetResourceReference(StyleProperty, "Pp.Caption");
        _healthUpdated.Text = "Analyse en cours…";
        AddSection("health", "Santé du PC", _healthUpdated, _healthRefresh);
        _stack.Children.Add(BuildHealthSummary());
        _stack.Children.Add(_healthList);
        ShowHealthSkeleton();

        // Recommandations
        AddSection("reco", "Recommandations pour ce PC",
            DashUi.Text("Réglages dont l'état actuel diffère de ce que Timonier conseille pour ce matériel. Rien n'est appliqué sans votre accord.", "Pp.Caption"),
            null);
        _stack.Children.Add(_recoHost);
        ShowRecoWaiting(AppHost.Profile.HardwareLoaded ? "Analyse des réglages…" : "En attente de l'analyse du matériel…", null);

        // Actions rapides
        _quickToggle = DashUi.Button("Afficher tout", null, "Pp.LinkButton");
        _quickToggle.FontSize = 13;
        _quickToggle.Click += (_, _) => { _quickAll = !_quickAll; RenderQuickActions(); };
        AddSection("quick", "Actions rapides", null, _quickToggle);
        _stack.Children.Add(_quickHost);
        RenderQuickActions();

        // Outils du fabricant
        _stack.Children.Add(_vendorSection);

        // Transparence
        AddSection("transparency", "Transparence", null, null);
        _stack.Children.Add(_transparencyHost);

        RenderIdentity();
        RenderVendorTools();
        RenderTransparency();

        _timer.Tick += async (_, _) => await TickAsync();
        _changedDebounce.Tick += async (_, _) =>
        {
            _changedDebounce.Stop();
            if (!IsVisible) return;
            await RefreshHealthAsync();
            await RefreshRecommendationsAsync();
        };
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += (_, _) => UpdateTimer();
    }

    // ================================================================== Cycle de vie

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        AppHost.HardwareLoaded += OnHardwareLoaded;
        AppHost.Engine.Changed += OnEngineChanged;
        _window = Window.GetWindow(this);
        if (_window is not null) _window.StateChanged += OnWindowStateChanged;

        UpdateGreeting();
        if (AppHost.Profile.HardwareLoaded && !_hardwareApplied) ApplyHardware();
        UpdateTimer();
        HandlePendingSection();

        var healthTask = _health is null || _healthStale || DateTime.Now - _healthAt > StaleAfter ? RefreshHealthAsync() : Task.CompletedTask;
        var recoTask = AppHost.Profile.HardwareLoaded && (!_recoDone || _recoStale || DateTime.Now - _recoAt > StaleAfter * 5)
            ? RefreshRecommendationsAsync() : Task.CompletedTask;
        await Task.WhenAll(healthTask, recoTask);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AppHost.HardwareLoaded -= OnHardwareLoaded;
        AppHost.Engine.Changed -= OnEngineChanged;
        if (_window is not null) _window.StateChanged -= OnWindowStateChanged;
        _window = null;
        _changedDebounce.Stop();
        StopTimer();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e) => UpdateTimer();

    /// <summary>Met à jour tout ce qui dépend du matériel (identité, outils du fabricant, disponibilité des réglages).</summary>
    private void ApplyHardware()
    {
        _hardwareApplied = true;
        RenderIdentity();
        RenderVendorTools();
        RenderTransparency();
    }

    private async void OnHardwareLoaded(object? sender, EventArgs e)
    {
        ApplyHardware();
        if (IsLoaded)
        {
            // La santé des disques et les recommandations dépendent du matériel.
            await RefreshHealthAsync();
            await RefreshRecommendationsAsync();
        }
    }

    private void OnEngineChanged(object? sender, TweakChangedEventArgs e) =>
        Dispatcher.InvokeAsync(() =>
        {
            _healthStale = _recoStale = true;
            _changedDebounce.Stop();
            _changedDebounce.Start();
        });

    // ================================================================== Navigation

    public void OnNavigatedTo(object? parameter)
    {
        if (parameter is string s && s.StartsWith("section:", StringComparison.Ordinal))
        {
            _pendingSection = s["section:".Length..];
            if (IsLoaded) HandlePendingSection();
        }
    }

    private void HandlePendingSection()
    {
        if (_pendingSection is not { } key) return;
        _pendingSection = null;
        if (key == "pc") { _scroll.ScrollToTop(); return; }
        if (!_anchors.TryGetValue(key, out var target) || target.Visibility != Visibility.Visible) return;
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var top = target.TransformToAncestor(_scroll).Transform(new Point(0, 0)).Y + _scroll.VerticalOffset;
                _scroll.ScrollToVerticalOffset(Math.Max(0, top - 8));
            }
            catch (InvalidOperationException) { target.BringIntoView(); }
        }, DispatcherPriority.Loaded);
    }

    private void AddSection(string key, string title, TextBlock? caption, FrameworkElement? right, Panel? host = null)
    {
        var header = DashUi.SectionHeader(title, caption, right);
        _anchors[key] = header;
        (host ?? _stack).Children.Add(header);
    }

    // ================================================================== Héros

    private Border BuildHero()
    {
        _greeting.SetResourceReference(StyleProperty, "Pp.PageTitle");
        _date.SetResourceReference(StyleProperty, "Pp.PageSubtitle");
        var head = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        head.Children.Add(_greeting);
        head.Children.Add(_date);

        // Pastille d'état global (cliquable : mène à la section Santé).
        _heroStatusIcon.SetResourceReference(StyleProperty, "Pp.Icon");
        _heroStatusIcon.FontSize = 14;
        _heroStatusIcon.Margin = new Thickness(0, 0, 8, 0);
        _heroStatusText.FontSize = 13;
        _heroStatusText.FontWeight = FontWeights.SemiBold;
        _heroStatusText.VerticalAlignment = VerticalAlignment.Center;
        var pillRow = new StackPanel { Orientation = Orientation.Horizontal };
        pillRow.Children.Add(_heroStatusIcon);
        pillRow.Children.Add(_heroStatusText);
        _heroStatus.Child = pillRow;
        _heroStatus.CornerRadius = new CornerRadius(16);
        _heroStatus.Padding = new Thickness(14, 7, 16, 7);
        _heroStatus.VerticalAlignment = VerticalAlignment.Center;
        _heroStatus.Cursor = System.Windows.Input.Cursors.Hand;
        _heroStatus.ToolTip = "Voir le détail de la santé du PC";
        _heroStatus.MouseLeftButtonUp += (_, _) => { _pendingSection = "health"; HandlePendingSection(); };
        SetHeroStatus(HealthStatus.Unknown, "Analyse en cours…");

        var top = new DockPanel();
        DockPanel.SetDock(_heroStatus, Dock.Right);
        top.Children.Add(_heroStatus);
        top.Children.Add(head);

        // Identité de la machine
        _pcName.SetResourceReference(StyleProperty, "Pp.CardTitle");
        _pcName.FontSize = 18;
        _pcName.FontWeight = FontWeights.SemiBold;
        _pcModel.SetResourceReference(StyleProperty, "Pp.Caption");
        _pcModel.FontSize = 13;
        _pcModel.Margin = new Thickness(0, 2, 0, 0);
        var idText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        idText.Children.Add(_pcName);
        idText.Children.Add(_pcModel);
        idText.Children.Add(_pcChips);
        var identity = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(_pcIcon, Dock.Left);
        identity.Children.Add(_pcIcon);
        identity.Children.Add(idText);

        var divider = new Border { Height = 1, Margin = new Thickness(0, 18, 0, 18) };
        divider.SetResourceReference(Border.BackgroundProperty, "Pp.Divider");

        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(divider);
        stack.Children.Add(identity);
        stack.Children.Add(_specsHost);
        var card = DashUi.Card(stack, new Thickness(24, 20, 24, 22));
        _anchors["pc"] = card;
        return card;
    }

    private void UpdateGreeting()
    {
        var hour = DateTime.Now.Hour;
        var hello = hour is >= 5 and < 18 ? "Bonjour" : "Bonsoir";
        var name = AppHost.Profile.UserName;
        if (name.Length > 0) name = char.ToUpper(name[0], Fr) + name[1..];
        _greeting.Text = name.Length > 0 ? $"{hello}, {name}" : hello;
        var d = DateTime.Now.ToString("dddd d MMMM yyyy", Fr);
        _date.Text = char.ToUpper(d[0], Fr) + d[1..] + " · voici l'essentiel de votre PC.";
    }

    private void SetHeroStatus(HealthStatus status, string text)
    {
        var v = DashUi.StatusVisual(status);
        _heroStatus.SetResourceReference(Border.BackgroundProperty, v.Bg);
        _heroStatusIcon.SetResourceReference(TextBlock.ForegroundProperty, v.Fg);
        _heroStatusText.SetResourceReference(TextBlock.ForegroundProperty, v.Fg);
        _heroStatusIcon.Text = status == HealthStatus.Unknown ? "" : v.Glyph;
        _heroStatusText.Text = text;
    }

    private void RenderIdentity()
    {
        var p = AppHost.Profile;
        UpdateGreeting();
        _pcName.Text = string.IsNullOrEmpty(p.MachineName) ? Environment.MachineName : p.MachineName;
        _pcIcon.Child = DashUi.Icon(p.IsLaptopLike ? "" : p.IsVirtualMachine ? "" : "", 26, "Pp.AccentText");

        _pcChips.Children.Clear();
        if (!p.HardwareLoaded)
        {
            _pcModel.Text = "Analyse du matériel…";
            var loading = new StackPanel();
            loading.Children.Add(SpecGrid(p, hardware: false));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            row.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 120, Height = 3, VerticalAlignment = VerticalAlignment.Center });
            var t = DashUi.Text("Analyse du matériel… (processeur, mémoire, carte graphique, disques)", "Pp.Caption");
            t.Margin = new Thickness(10, 0, 0, 0);
            row.Children.Add(t);
            loading.Children.Add(row);
            _specsHost.Content = loading;
            return;
        }

        var model = p.Model.Trim();
        _pcModel.Text = string.IsNullOrEmpty(model) || model.Equals(p.Manufacturer, StringComparison.OrdinalIgnoreCase)
            ? p.Manufacturer
            : $"{p.Manufacturer} · {model}";
        _pcChips.Children.Add(DashUi.Badge(p.FormFactorLabel));
        if (p.Tier != PerformanceTier.Unknown)
            _pcChips.Children.Add(DashUi.Badge("Gamme " + p.TierLabel.ToLower(Fr), "Pp.AccentText", "Pp.AccentSubtle"));
        if (p.HasTouch) _pcChips.Children.Add(DashUi.Badge("Écran tactile"));
        if (p.IsVirtualMachine) _pcChips.Children.Add(DashUi.Badge("Machine virtuelle", "Pp.Info", "Pp.InfoBackground"));
        if (p.IsManaged) _pcChips.Children.Add(DashUi.Badge("Géré par une organisation", "Pp.Warning", "Pp.WarningBackground"));
        _specsHost.Content = SpecGrid(p, hardware: true);
    }

    private static Grid SpecGrid(SystemProfile p, bool hardware)
    {
        var items = new List<UIElement>
        {
            Spec("", "Windows",
                $"Windows {(p.IsWindows11 ? "11" : "10")} {p.EditionLabel}",
                $"Version {p.DisplayVersion} · build {p.Build}.{p.Ubr}{(string.IsNullOrEmpty(p.Architecture) ? "" : " · " + p.Architecture)}"),
        };
        if (!hardware)
        {
            items.Add(Spec("", "Session", p.UserName, p.IsUserAdmin ? "Compte administrateur" : "Compte standard"));
            return DashUi.Columns(items, 4, 16);
        }

        var cpuName = CleanCpu(p.CpuName);
        var (cores, threads) = CpuCounts(p);
        items.Add(Spec("", "Processeur", cpuName.Length > 0 ? cpuName : "Inconnu",
            cores > 0 ? $"{cores} cœur{(cores > 1 ? "s" : "")} · {threads} thread{(threads > 1 ? "s" : "")}" : $"{threads} processeurs logiques"));
        items.Add(Spec("", "Mémoire vive", p.RamGb > 0 ? p.RamGb.ToString("0.#", Fr) + " Go" : "Inconnue",
            p.Tier == PerformanceTier.Unknown ? "" : "PC " + p.TierLabel.ToLower(Fr)));

        var gpus = p.Gpus.Where(g => !string.IsNullOrWhiteSpace(g.Name)).ToList();
        items.Add(Spec("", gpus.Count > 1 ? "Cartes graphiques" : "Carte graphique",
            gpus.Count == 0 ? "Inconnue" : string.Join(" + ", gpus.Select(g => CleanCpu(g.Name))),
            gpus.Count == 0 ? "" : string.Join(" · ", gpus.Select(g => g.Integrated ? "intégrée" : "dédiée").Distinct())));

        var disk = p.SystemDisk;
        items.Add(Spec("", "Disque système",
            disk is null ? "Inconnu" : $"{MediaLabel(disk)} · {Format.Bytes(disk.SizeBytes)}",
            disk is null ? "" : disk.Model.Trim()));

        items.Add(Spec(p.HasBattery ? "" : "", "Alimentation",
            p.HasBattery ? "Batterie présente" : "Sur secteur uniquement",
            p.FormFactorLabel));

        var secure = p.SecureBoot switch { true => "Secure Boot activé", false => "Secure Boot désactivé", _ => "Secure Boot : état inconnu" };
        items.Add(Spec("", "Micrologiciel", p.IsUefi ? "UEFI" : "BIOS hérité (Legacy)", p.IsUefi ? secure : "Secure Boot indisponible"));

        var managed = new List<string>();
        if (p.IsDomainJoined) managed.Add("domaine Active Directory");
        if (p.IsEntraJoined) managed.Add("Microsoft Entra ID");
        if (p.IsMdmManaged) managed.Add("gestion MDM (Intune…)");
        items.Add(Spec("", "Gestion",
            managed.Count == 0 ? "PC non géré" : "Géré : " + string.Join(", ", managed),
            (p.IsUserAdmin ? "Votre compte est administrateur" : "Votre compte est standard") + $" ({p.UserName})"));
        return DashUi.Columns(items, 4, 16);
    }

    private static FrameworkElement Spec(string glyph, string label, string value, string detail)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var icon = DashUi.Icon(glyph, 13, "Pp.TextSecondary");
        icon.Margin = new Thickness(0, 0, 7, 0);
        head.Children.Add(icon);
        head.Children.Add(DashUi.Text(label, "Pp.Caption"));
        var v = DashUi.Text(value, "Pp.Body", 13, semiBold: true);
        v.TextWrapping = TextWrapping.Wrap;
        var s = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        s.Children.Add(head);
        s.Children.Add(v);
        if (!string.IsNullOrWhiteSpace(detail))
        {
            var d = DashUi.Text(detail, "Pp.Caption");
            d.Margin = new Thickness(0, 2, 0, 0);
            s.Children.Add(d);
        }
        return s;
    }

    /// <summary>
    /// Nombre de cœurs et de threads. Le cache du profil peut cumuler les valeurs d'une exécution à l'autre : le nombre de
    /// processeurs logiques vu par .NET fait foi et le nombre de cœurs est ramené à la même échelle.
    /// </summary>
    private static (int Cores, int Threads) CpuCounts(SystemProfile p)
    {
        var threads = Environment.ProcessorCount;
        var cores = p.CpuCores;
        if (cores <= 0) return (0, threads);
        if (p.CpuThreads > threads && p.CpuThreads % threads == 0) cores = cores * threads / p.CpuThreads;
        return (Math.Clamp(cores, 1, threads), threads);
    }

    private static string CleanCpu(string name) =>
        name.Replace("(R)", "", StringComparison.OrdinalIgnoreCase).Replace("(TM)", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" CPU", "", StringComparison.Ordinal).Replace("  ", " ").Trim();

    /// <summary>Type du disque, avec la même détection de l'eMMC que la page Performance (Windows la classe en « SSD »).</summary>
    private static string MediaLabel(DiskInfo d) => Performance.HardwareAdvice.IsEmmc(d) ? "Mémoire eMMC" : d.Media switch
    {
        DiskMedia.Nvme => "SSD NVMe",
        DiskMedia.Ssd => "SSD",
        DiskMedia.Hdd => "Disque dur (HDD)",
        _ => "Disque",
    };

    // ================================================================== En direct

    private void UpdateTimer()
    {
        var visible = IsLoaded && IsVisible && _window?.WindowState != WindowState.Minimized;
        if (visible && !_timer.IsEnabled)
        {
            _metrics.Reset();
            _timer.Interval = TimeSpan.FromMilliseconds(700); // première mesure rapide, puis toutes les 2 s
            _timer.Start();
            _ = TickAsync();
        }
        else if (!visible) StopTimer();
    }

    private void StopTimer()
    {
        _timer.Stop();
    }

    private async Task TickAsync()
    {
        if (_sampling) return;
        _sampling = true;
        try
        {
            var sample = await Task.Run(() => _metrics.Sample());
            if (_timer.Interval != TimeSpan.FromSeconds(2) && sample.CpuRatio is not null) _timer.Interval = TimeSpan.FromSeconds(2);
            RenderLive(sample);
        }
        catch (Exception ex)
        {
            Log.Warn("Dashboard", "mesures en direct : " + ex.Message);
        }
        finally { _sampling = false; }
    }

    private void BuildLiveTiles(bool hasBattery)
    {
        _liveHasBattery = hasBattery;
        _cpuTile = new LiveTile("", "Processeur", ring: true);
        _ramTile = new LiveTile("", "Mémoire", ring: true);
        _diskTile = new LiveTile("", "Disque système", bar: true);
        _batteryTile = hasBattery ? new LiveTile("", "Batterie", bar: true) : null;
        _netTile = new LiveTile("", "Réseau");
        _uptimeTile = new LiveTile("", "Allumé depuis");
        var tiles = new List<UIElement> { _cpuTile, _ramTile, _diskTile };
        if (_batteryTile is not null) tiles.Add(_batteryTile);
        tiles.Add(_netTile);
        tiles.Add(_uptimeTile);
        _liveHost.Content = DashUi.Columns(tiles, tiles.Count == 5 ? 5 : 3, 8);
    }

    private void RenderLive(LiveSample s)
    {
        if (_liveHasBattery != s.HasBattery || _cpuTile is null) BuildLiveTiles(s.HasBattery);

        if (s.CpuRatio is { } cpu)
            _cpuTile!.Set(Format.Percent(cpu), cpu >= 0.9 ? "Très sollicité" : cpu >= 0.6 ? "Assez sollicité" : "Utilisation normale",
                cpu, cpu >= 0.9 ? "Pp.Danger" : cpu >= 0.75 ? "Pp.Warning" : "Pp.Accent");
        else _cpuTile!.Set("…", "Mesure en cours", 0, "Pp.Accent");

        if (s.RamTotal > 0)
        {
            var r = (double)s.RamUsed / s.RamTotal;
            _ramTile!.Set(Format.Percent(r), $"{Format.Bytes((long)s.RamUsed)} utilisés sur {Format.Bytes((long)s.RamTotal)}",
                r, r >= 0.92 ? "Pp.Danger" : r >= 0.8 ? "Pp.Warning" : "Pp.Accent");
        }

        if (s.DriveTotal > 0)
        {
            var freeRatio = (double)s.DriveFree / s.DriveTotal;
            _diskTile!.Set($"{Format.Bytes(s.DriveFree)} libres", $"{s.DriveName} · {Format.Percent(1 - freeRatio)} utilisés sur {Format.Bytes(s.DriveTotal)}",
                1 - freeRatio, freeRatio < 0.05 ? "Pp.Danger" : freeRatio < 0.10 ? "Pp.Warning" : "Pp.Accent");
        }
        else _diskTile!.Set("Indisponible", "Lecteur système illisible", 0, "Pp.Accent");

        if (_batteryTile is not null)
        {
            var pct = s.BatteryPercent;
            string state = s.Charging ? "En charge"
                : s.OnAc == true ? "Branché, charge en pause"
                : s.BatteryRemaining is { } left ? $"Sur batterie · environ {Format.Duration(left)} restantes"
                : "Sur batterie";
            var brush = s.Charging || s.OnAc == true ? "Pp.Success" : pct < 10 ? "Pp.Danger" : pct < 20 ? "Pp.Warning" : "Pp.Accent";
            _batteryTile.Set(pct is { } v ? v + " %" : "Inconnu", state, (pct ?? 0) / 100.0, brush);
        }

        _netTile!.Set(s.NetworkAvailable ? "Connecté" : "Hors ligne",
            s.NetworkAvailable ? "Une connexion réseau est active" : "Aucune connexion réseau active",
            null, s.NetworkAvailable ? "Pp.Success" : "Pp.Danger");

        var boot = DateTime.Now - s.Uptime;
        _uptimeTile!.Set(Format.Duration(s.Uptime), "Depuis le " + boot.ToString("d MMMM à HH:mm", Fr), null,
            s.Uptime.TotalDays > 7 ? "Pp.Warning" : "Pp.Accent");
    }

    // ================================================================== Santé

    private Border BuildHealthSummary()
    {
        _healthHeadline.SetResourceReference(StyleProperty, "Pp.CardTitle");
        _healthHeadline.FontSize = 20;
        _healthHeadline.FontWeight = FontWeights.SemiBold;
        _healthHeadline.Text = "Analyse en cours…";
        _healthSummary.SetResourceReference(StyleProperty, "Pp.Caption");
        _healthSummary.FontSize = 13;
        _healthSummary.Margin = new Thickness(0, 3, 0, 0);
        _healthSummary.Text = "Vérification de la sécurité, de l'espace disque, des redémarrages en attente et du matériel.";
        _healthIcon.Content = DashUi.IconBox("", 48, 22, "Pp.Neutral", "Pp.NeutralBackground", 24);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        text.Children.Add(_healthHeadline);
        text.Children.Add(_healthSummary);
        text.Children.Add(_healthProgress);
        var dock = new DockPanel();
        DockPanel.SetDock(_healthIcon, Dock.Left);
        dock.Children.Add(_healthIcon);
        dock.Children.Add(text);
        var card = DashUi.Card(dock, new Thickness(18, 16, 18, 16));
        card.Margin = new Thickness(0, 0, 0, 8);
        return card;
    }

    private void ShowHealthSkeleton()
    {
        var items = new List<UIElement>();
        for (var i = 0; i < 4; i++)
        {
            var s = new StackPanel();
            s.Children.Add(DashUi.Skeleton(140, 12, new Thickness(0, 2, 0, 8)));
            s.Children.Add(DashUi.Skeleton(220, 10, new Thickness(0)));
            var dock = new DockPanel();
            var circle = DashUi.Skeleton(32, 32, new Thickness(0, 0, 12, 0));
            circle.CornerRadius = new CornerRadius(16);
            DockPanel.SetDock(circle, Dock.Left);
            dock.Children.Add(circle);
            dock.Children.Add(s);
            items.Add(DashUi.Card(dock));
        }
        _healthList.Content = DashUi.Columns(items, 2);
    }

    private async Task RefreshHealthAsync()
    {
        if (_healthBusy) return;
        _healthBusy = true;
        _healthStale = false;
        _healthRefresh.IsEnabled = false;
        _healthProgress.Visibility = Visibility.Visible;
        _healthUpdated.Text = "Analyse en cours…";
        try
        {
            var checks = AppHost.Registry.HealthChecks.ToList();
            var results = await Task.WhenAll(checks.Select(RunCheckAsync));
            _health = [.. checks.Zip(results)];
            _healthAt = DateTime.Now;
            RenderHealth();
        }
        catch (Exception ex)
        {
            Log.Error("Dashboard", "contrôles de santé", ex);
            _healthHeadline.Text = "Analyse impossible";
            _healthSummary.Text = ex.Message;
        }
        finally
        {
            _healthBusy = false;
            _healthRefresh.IsEnabled = true;
            _healthProgress.Visibility = Visibility.Collapsed;
        }
    }

    private static async Task<HealthResult> RunCheckAsync(IHealthCheck check)
    {
        using var cts = new CancellationTokenSource(HealthTimeout);
        try
        {
            var task = Task.Run(() => check.CheckAsync(cts.Token));
            var finished = await Task.WhenAny(task, Task.Delay(HealthTimeout)).ConfigureAwait(false);
            if (finished != task)
            {
                cts.Cancel();
                _ = task.ContinueWith(t => _ = t.Exception, TaskScheduler.Default); // observe une éventuelle erreur tardive
                return new HealthResult(HealthStatus.Unknown, "Délai dépassé (5 s)", "Ce contrôle n'a pas répondu à temps ; réessayez avec « Actualiser ».");
            }
            return await task.ConfigureAwait(false) ?? new HealthResult(HealthStatus.Unknown, "Aucun résultat");
        }
        catch (OperationCanceledException)
        {
            return new HealthResult(HealthStatus.Unknown, "Délai dépassé (5 s)");
        }
        catch (Exception ex)
        {
            Log.Warn("Dashboard", $"contrôle {check.Id} : {ex.Message}");
            return new HealthResult(HealthStatus.Unknown, "Contrôle impossible", ex.Message);
        }
    }

    private void RenderHealth()
    {
        var list = _health ?? [];
        _healthUpdated.Text = $"{list.Count} contrôle{(list.Count > 1 ? "s" : "")} · analysé à {_healthAt:HH:mm}";
        if (list.Count == 0)
        {
            _healthHeadline.Text = "Aucun contrôle disponible";
            _healthSummary.Text = "Les modules installés ne proposent pas de contrôle de santé.";
            _healthIcon.Content = DashUi.IconBox("", 48, 22, "Pp.Neutral", "Pp.NeutralBackground", 24);
            _healthList.Content = null;
            SetHeroStatus(HealthStatus.Unknown, "Aucun contrôle");
            return;
        }

        var critical = list.Count(x => x.Result.Status == HealthStatus.Critical);
        var warning = list.Count(x => x.Result.Status == HealthStatus.Warning);
        var good = list.Count(x => x.Result.Status == HealthStatus.Good);
        var info = list.Count(x => x.Result.Status == HealthStatus.Info);
        var unknown = list.Count(x => x.Result.Status == HealthStatus.Unknown);
        var issues = critical + warning;

        HealthStatus overall;
        string headline;
        if (critical > 0)
        {
            overall = HealthStatus.Critical;
            headline = critical == 1 ? "1 problème important" : $"{critical} problèmes importants";
            if (warning > 0) headline += $" et {warning} point{(warning > 1 ? "s" : "")} à vérifier";
        }
        else if (warning > 0)
        {
            overall = HealthStatus.Warning;
            headline = warning == 1 ? "1 point à vérifier" : $"{warning} points à vérifier";
        }
        else
        {
            overall = HealthStatus.Good;
            headline = "Tout va bien";
        }

        var parts = new List<string>();
        if (good > 0) parts.Add($"{good} OK");
        if (info > 0) parts.Add($"{info} information{(info > 1 ? "s" : "")}");
        if (issues > 0) parts.Add($"{issues} à traiter");
        if (unknown > 0) parts.Add($"{unknown} indéterminé{(unknown > 1 ? "s" : "")}");
        _healthHeadline.Text = headline;
        _healthSummary.Text = string.Join(" · ", parts) + (overall == HealthStatus.Good
            ? ". Aucun problème détecté par les contrôles de Timonier."
            : ". Les éléments à traiter sont affichés en premier.");
        var v = DashUi.StatusVisual(overall);
        _healthIcon.Content = DashUi.IconBox(v.Glyph, 48, 22, v.Fg, v.Bg, 24);
        SetHeroStatus(overall, headline);

        var ordered = list
            .OrderBy(x => DashUi.Severity(x.Result.Status))
            .ThenBy(x => x.Check.Title, StringComparer.Create(Fr, true))
            .ToList();
        // Les contrôles réussis sont repliés quand d'autres points méritent l'attention : l'essentiel reste visible.
        var fold = good >= 3 && good < list.Count;
        var shown = fold && !_healthShowGood ? ordered.Where(x => x.Result.Status != HealthStatus.Good).ToList() : ordered;
        var host = new StackPanel();
        host.Children.Add(DashUi.Columns([.. shown.Select(x => (UIElement)HealthItem(x.Check, x.Result))], 2));
        if (fold)
        {
            var toggle = DashUi.Button(_healthShowGood
                    ? "Masquer les contrôles réussis"
                    : $"Afficher les {good} contrôles réussis",
                _healthShowGood ? "" : "", "Pp.SubtleButton");
            toggle.HorizontalAlignment = HorizontalAlignment.Left;
            toggle.Margin = new Thickness(0, 8, 0, 0);
            toggle.Click += (_, _) => { _healthShowGood = !_healthShowGood; RenderHealth(); };
            host.Children.Add(toggle);
        }
        _healthList.Content = host;
    }

    private Border HealthItem(IHealthCheck check, HealthResult result)
    {
        var v = DashUi.StatusVisual(result.Status);
        var badge = DashUi.IconBox(v.Glyph, 32, 15, v.Fg, v.Bg, 16);
        badge.Margin = new Thickness(0, 1, 12, 0);
        badge.ToolTip = v.Label;

        var title = DashUi.Text(check.Title, "Pp.Body", 14, semiBold: true);
        var summary = DashUi.Text(result.Summary, "Pp.Caption", 13);
        summary.Margin = new Thickness(0, 2, 0, 0);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(title);
        text.Children.Add(summary);
        if (!string.IsNullOrWhiteSpace(result.Detail))
        {
            var detail = DashUi.Text(result.Detail, "Pp.Caption", 12, brush: "Pp.TextTertiary");
            detail.Margin = new Thickness(0, 4, 0, 0);
            detail.MaxHeight = 50;
            detail.TextTrimming = TextTrimming.CharacterEllipsis;
            detail.ToolTip = result.Detail;
            text.Children.Add(detail);
        }

        var dock = new DockPanel();
        DockPanel.SetDock(badge, Dock.Left);
        dock.Children.Add(badge);
        if (ActionFor(check, result) is { } button)
        {
            button.Margin = new Thickness(12, 0, 0, 0);
            button.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(button, Dock.Right);
            dock.Children.Add(button);
        }
        dock.Children.Add(text);
        var card = DashUi.Card(dock, new Thickness(14, 12, 14, 12));
        card.Margin = new Thickness(0);
        return card;
    }

    private Button? ActionFor(IHealthCheck check, HealthResult result)
    {
        var problem = result.Status is HealthStatus.Warning or HealthStatus.Critical;
        if (check.Id is "dashboard.pending-reboot" or "dashboard.uptime" && result.Status is not (HealthStatus.Good or HealthStatus.Unknown))
        {
            var reboot = DashUi.Button("Redémarrer…", null, problem ? "Pp.AccentButton" : "Pp.Button");
            reboot.Click += async (_, _) =>
            {
                var ok = await AppHost.Dialogs.ConfirmAsync("Redémarrer maintenant ?",
                    "Windows redémarrera dans 5 secondes. Enregistrez votre travail et fermez vos documents avant de continuer.",
                    "Redémarrer", "Plus tard", danger: true);
                if (!ok) return;
                reboot.IsEnabled = false;
                try { await SystemEffects.RebootNowAsync(); }
                catch (Exception ex) { AppHost.Toasts.Show("Redémarrage impossible : " + ex.Message, ToastKind.Error); reboot.IsEnabled = true; }
            };
            return reboot;
        }
        if (check.Id == "dashboard.disk-space" && AppHost.Registry.GetPage("maintenance") is null && problem)
        {
            var storage = DashUi.Button("Stockage…", null, "Pp.AccentButton");
            storage.ToolTip = "Ouvre Paramètres › Système › Stockage (assistant de stockage, fichiers temporaires).";
            storage.Click += (_, _) => { try { ProcessRunner.OpenSettingsUri("ms-settings:storagesense"); } catch (Exception ex) { AppHost.Toasts.Show(ex.Message, ToastKind.Error); } };
            return storage;
        }

        if (string.IsNullOrEmpty(check.PageId) || check.PageId == DashboardModule.PageId) return null;
        if (!PageExists(check.PageId)) return null;
        var button = DashUi.Button(problem ? "Corriger" : "Voir", null, problem ? "Pp.AccentButton" : "Pp.Button");
        var pageId = check.PageId;
        button.Click += (_, _) => AppHost.Navigator.Navigate(pageId);
        return button;
    }

    private static bool PageExists(string pageId) =>
        AppHost.Registry.GetPage(pageId) is not null
        || pageId.StartsWith("category:", StringComparison.Ordinal) && AppHost.Registry.GetCategory(pageId["category:".Length..]) is not null;

    // ================================================================== Recommandations

    private void ShowRecoWaiting(string message, (int Done, int Total)? progress)
    {
        var stack = new StackPanel();
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var bar = new ProgressBar { Width = 160, Height = 3, VerticalAlignment = VerticalAlignment.Center };
        if (progress is { Total: > 0 } p) { bar.Maximum = p.Total; bar.Value = p.Done; }
        else bar.IsIndeterminate = true;
        DockPanel.SetDock(bar, Dock.Right);
        row.Children.Add(bar);
        row.Children.Add(DashUi.Text(progress is { Total: > 0 } q ? $"{message} {q.Done} / {q.Total}" : message, "Pp.Caption", 13));
        stack.Children.Add(row);
        for (var i = 0; i < 3; i++)
        {
            var line = new DockPanel { Margin = new Thickness(0, i == 0 ? 0 : 12, 0, 0) };
            var box = DashUi.Skeleton(32, 32, new Thickness(0, 0, 12, 0));
            DockPanel.SetDock(box, Dock.Left);
            line.Children.Add(box);
            var s = new StackPanel();
            s.Children.Add(DashUi.Skeleton(180 - i * 30, 12, new Thickness(0, 2, 0, 8)));
            s.Children.Add(DashUi.Skeleton(320 - i * 40, 10, new Thickness(0)));
            line.Children.Add(s);
            stack.Children.Add(line);
        }
        _recoHost.Content = DashUi.Card(stack, new Thickness(18, 16, 18, 16));
    }

    private async Task RefreshRecommendationsAsync()
    {
        if (_recoBusy || !AppHost.Profile.HardwareLoaded) return;
        _recoBusy = true;
        _recoStale = false;
        try
        {
            var profile = AppHost.Profile;
            var advanced = AppHost.Settings.AdvancedMode;
            var candidates = AppHost.Registry.Tweaks
                .Where(t => t.Kind != TweakKind.Action && (advanced || t.Risk != RiskLevel.Advanced))
                .Select(t => (Tweak: t, Reco: SafeRecommendation(t, profile)))
                .Where(x => x.Reco is not null && x.Tweak.GetOption(x.Reco) is not null && AppHost.Engine.Unavailability(x.Tweak) is null)
                .ToList();
            if (candidates.Count == 0)
            {
                _recoDone = true;
                _recoAt = DateTime.Now;
                RenderRecommendations([], 0);
                return;
            }

            var total = candidates.Count;
            ShowRecoWaiting("Analyse des réglages…", (0, total));
            var progress = new Progress<int>(done => { if (_recoBusy) ShowRecoWaiting("Analyse des réglages…", (done, total)); });
            IProgress<int> report = progress;
            var done = 0;
            using var gate = new SemaphoreSlim(4);
            var engine = AppHost.Engine;
            var states = await Task.WhenAll(candidates.Select(async c =>
            {
                await gate.WaitAsync().ConfigureAwait(false);
                try { return await engine.DetectAsync(c.Tweak).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    Log.Warn("Dashboard", $"détection {c.Tweak.Id} : {ex.Message}");
                    return TweakState.UnknownState;
                }
                finally
                {
                    gate.Release();
                    var n = Interlocked.Increment(ref done);
                    if (n % 25 == 0) report.Report(n);
                }
            }));

            var mismatches = candidates.Zip(states)
                .Where(x => !x.Second.Unknown && (x.Second.OptionKey != x.First.Reco || x.Second.Partial))
                .Select(x => x.First.Tweak)
                .ToList();
            _recoDone = true;
            _recoAt = DateTime.Now;
            RenderRecommendations(mismatches, total);
        }
        catch (Exception ex)
        {
            Log.Error("Dashboard", "recommandations", ex);
            _recoHost.Content = DashUi.Card(DashUi.Text("Impossible d'analyser les réglages : " + ex.Message, "Pp.Body"));
        }
        finally { _recoBusy = false; }
    }

    private static string? SafeRecommendation(TweakDefinition t, SystemProfile p)
    {
        try { return t.RecommendationFor(p); }
        catch { return null; }
    }

    private void RenderRecommendations(List<TweakDefinition> mismatches, int analysed)
    {
        var p = AppHost.Profile;
        var stack = new StackPanel();

        var profileBits = new List<string>();
        if (p.Tier != PerformanceTier.Unknown) profileBits.Add("gamme " + p.TierLabel.ToLower(Fr));
        profileBits.Add(p.IsLaptopLike ? "PC portable" : "PC de bureau");
        if (p.SystemDisk is { } d && d.Media != DiskMedia.Unknown)
            profileBits.Add(d.Media == DiskMedia.Hdd ? "disque dur" : Performance.HardwareAdvice.IsEmmc(d) ? "mémoire eMMC" : "SSD");
        profileBits.Add("Windows " + (p.IsWindows11 ? "11 " : "10 ") + p.EditionLabel);

        if (mismatches.Count == 0)
        {
            var dock = new DockPanel();
            var icon = DashUi.IconBox("", 40, 18, "Pp.Success", "Pp.SuccessBackground", 20);
            icon.Margin = new Thickness(0, 0, 14, 0);
            DockPanel.SetDock(icon, Dock.Left);
            dock.Children.Add(icon);
            var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            t.Children.Add(DashUi.Text(analysed == 0 ? "Aucun réglage à analyser" : "Ce PC suit déjà toutes les recommandations", "Pp.Body", 15, semiBold: true));
            t.Children.Add(DashUi.Text(analysed == 0
                ? "Aucun module de réglages n'est installé, ou aucun réglage ne s'applique à ce PC."
                : $"{analysed} réglages vérifiés pour ce profil : {string.Join(", ", profileBits)}.", "Pp.Caption", 13));
            dock.Children.Add(t);
            _recoHost.Content = DashUi.Card(dock, new Thickness(18, 16, 18, 16));
            return;
        }

        // En-tête : total
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var number = DashUi.Text(mismatches.Count.ToString(Fr), "Pp.Metric", 30);
        number.Margin = new Thickness(0, 0, 14, 0);
        number.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(number, Dock.Left);
        head.Children.Add(number);
        var headText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        headText.Children.Add(DashUi.Text(mismatches.Count == 1 ? "réglage peut être ajusté" : "réglages peuvent être ajustés", "Pp.Body", 15, semiBold: true));
        headText.Children.Add(DashUi.Text($"Sur {analysed} réglages vérifiés, adaptés à ce profil : {string.Join(", ", profileBits)}.", "Pp.Caption", 13));
        head.Children.Add(headText);
        stack.Children.Add(head);

        var groups = mismatches
            .GroupBy(t => t.Category)
            .Select(g => (Category: AppHost.Registry.GetCategory(g.Key), Id: g.Key, Items: g.ToList()))
            .OrderByDescending(g => g.Items.Count)
            .ThenBy(g => g.Category?.Title ?? g.Id, StringComparer.Create(Fr, true))
            .ToList();
        var first = true;
        foreach (var (category, id, items) in groups)
        {
            if (!first)
            {
                var sep = new Border { Height = 1, Margin = new Thickness(46, 10, 0, 10) };
                sep.SetResourceReference(Border.BackgroundProperty, "Pp.Divider");
                stack.Children.Add(sep);
            }
            first = false;
            stack.Children.Add(RecoRow(category, id, items));
        }
        _recoHost.Content = DashUi.Card(stack, new Thickness(18, 16, 18, 16));
    }

    private FrameworkElement RecoRow(CategoryInfo? category, string id, List<TweakDefinition> items)
    {
        var glyph = string.IsNullOrEmpty(category?.Glyph) ? "" : category!.Glyph;
        var icon = DashUi.IconBox(glyph, 34, 16);
        icon.Margin = new Thickness(0, 0, 12, 0);

        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(DashUi.Text(category?.Title ?? id, "Pp.Body", 14, semiBold: true));
        var count = DashUi.Badge(items.Count.ToString(Fr), "Pp.AccentText", "Pp.AccentSubtle");
        count.Margin = new Thickness(8, 0, 0, 0);
        title.Children.Add(count);
        var admin = items.Count(t => t.RequiresAdmin);
        if (admin > 0)
        {
            var shield = DashUi.Icon("", 12, "Pp.TextTertiary");
            shield.Margin = new Thickness(8, 0, 0, 0);
            shield.ToolTip = admin == items.Count ? "Nécessitent les droits administrateur" : $"{admin} nécessitent les droits administrateur";
            title.Children.Add(shield);
        }

        var names = items.Take(3).Select(t => t.Title).ToList();
        var sample = string.Join(", ", names) + (items.Count > 3 ? $" et {items.Count - 3} autre{(items.Count - 3 > 1 ? "s" : "")}" : "");
        var caption = DashUi.Text(sample, "Pp.Caption", 12.5);
        caption.Margin = new Thickness(0, 3, 0, 0);
        caption.TextTrimming = TextTrimming.CharacterEllipsis;
        caption.TextWrapping = TextWrapping.NoWrap;
        caption.ToolTip = string.Join("\n", items.Select(t => "• " + t.Title));

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(title);
        text.Children.Add(caption);

        var pageId = AppHost.Registry.PageIdForCategory(id);
        var button = DashUi.Button("Voir", null, "Pp.Button");
        button.Margin = new Thickness(12, 0, 0, 0);
        button.VerticalAlignment = VerticalAlignment.Center;
        button.Click += (_, _) => AppHost.Navigator.Navigate(pageId);

        var dock = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(button, Dock.Right);
        dock.Children.Add(icon);
        dock.Children.Add(button);
        dock.Children.Add(text);
        return dock;
    }

    // ================================================================== Actions rapides

    private void RenderQuickActions()
    {
        var all = AppHost.Registry.QuickActions
            .OrderBy(q => q.Order)
            .ThenBy(q => q.Title, StringComparer.Create(Fr, true))
            .ToList();
        _quickToggle.Visibility = all.Count > QuickPreview ? Visibility.Visible : Visibility.Collapsed;
        _quickToggle.Content = _quickAll ? "Afficher moins" : $"Afficher tout ({all.Count})";
        if (all.Count == 0)
        {
            _quickHost.Content = DashUi.Card(DashUi.Text("Aucune action rapide n'est disponible.", "Pp.Caption", 13));
            return;
        }
        var shown = _quickAll ? all : all.Take(QuickPreview).ToList();
        _quickHost.Content = DashUi.Columns([.. shown.Select(QuickCard)], 4, 8);
    }

    private UIElement QuickCard(QuickAction action)
    {
        var button = new Button { Style = DashUi.CardButtonStyle, ToolTip = action.Description };
        var glyph = string.IsNullOrEmpty(action.Glyph) ? "" : action.Glyph;
        var icon = DashUi.IconBox(glyph, 32, 16);
        icon.HorizontalAlignment = HorizontalAlignment.Left;

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        if (action.RequiresAdmin)
        {
            var shield = DashUi.Icon("", 12, "Pp.TextTertiary");
            shield.VerticalAlignment = VerticalAlignment.Top;
            shield.ToolTip = "Demande les droits administrateur";
            DockPanel.SetDock(shield, Dock.Right);
            top.Children.Add(shield);
        }
        top.Children.Add(icon);

        var title = DashUi.Text(action.Title, "Pp.Body", 13.5, semiBold: true);
        title.TextWrapping = TextWrapping.Wrap;
        var desc = DashUi.Text(action.Description, "Pp.Caption", 12);
        desc.Margin = new Thickness(0, 3, 0, 0);
        desc.MaxHeight = 48;
        desc.TextTrimming = TextTrimming.CharacterEllipsis;

        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(title);
        stack.Children.Add(desc);
        button.Content = stack;
        System.Windows.Automation.AutomationProperties.SetName(button, action.Title);

        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await action.Execute(); }
            catch (Exception ex)
            {
                Log.Error("Dashboard", "action rapide " + action.Id, ex);
                AppHost.Toasts.Show($"« {action.Title} » a échoué : {ex.Message}", ToastKind.Error);
            }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    // ================================================================== Outils du fabricant

    private void RenderVendorTools()
    {
        _vendorSection.Children.Clear();
        var p = AppHost.Profile;
        var tools = p.HardwareLoaded ? VendorTools.For(p) : [];
        if (tools.Count == 0)
        {
            _vendorSection.Visibility = Visibility.Collapsed;
            return;
        }
        var appsPage = AppHost.Registry.GetPage("apps") is not null;
        var origin = p.Manufacturer is { Length: > 0 } m && !m.StartsWith("Fabricant", StringComparison.Ordinal) ? $" ({m})" : "";
        AddSection("vendor", "Outils du fabricant",
            DashUi.Text($"Logiciels officiels conseillés d'après le fabricant de ce PC{origin} et de sa carte graphique. Timonier n'installe rien sans votre accord.", "Pp.Caption"),
            null, _vendorSection);

        var stack = new StackPanel();
        for (var i = 0; i < tools.Count; i++)
        {
            if (i > 0)
            {
                var sep = new Border { Height = 1, Margin = new Thickness(48, 12, 0, 12) };
                sep.SetResourceReference(Border.BackgroundProperty, "Pp.Divider");
                stack.Children.Add(sep);
            }
            stack.Children.Add(VendorRow(tools[i], appsPage));
        }
        _vendorSection.Children.Add(DashUi.Card(stack, new Thickness(18, 16, 18, 16)));
        _vendorSection.Visibility = Visibility.Visible;
    }

    private static FrameworkElement VendorRow(VendorTool tool, bool appsPage)
    {
        var icon = DashUi.IconBox(tool.Glyph, 36, 17);
        icon.Margin = new Thickness(0, 0, 12, 0);

        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(DashUi.Text(tool.Name, "Pp.Body", 14, semiBold: true));
        var publisher = DashUi.Badge(tool.Publisher);
        publisher.Margin = new Thickness(8, 0, 0, 0);
        title.Children.Add(publisher);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(title);
        var desc = DashUi.Text(tool.Description, "Pp.Caption", 12.5);
        desc.Margin = new Thickness(0, 3, 0, 0);
        text.Children.Add(desc);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        if (appsPage)
        {
            var see = DashUi.Button("Voir dans Applications", null, "Pp.Button");
            see.ToolTip = "Rechercher « " + tool.Search + " » dans la page Applications (installation via winget)";
            see.Click += (_, _) => AppHost.Navigator.Navigate("apps", "search:" + tool.Search);
            buttons.Children.Add(see);
        }
        if (tool.StoreId is { } id)
        {
            var store = DashUi.Button("Microsoft Store", "", "Pp.SubtleButton");
            store.Margin = new Thickness(6, 0, 0, 0);
            store.ToolTip = "Ouvrir la fiche de l'application dans le Microsoft Store";
            store.Click += (_, _) =>
            {
                try { ProcessRunner.OpenSettingsUri("ms-windows-store://pdp/?ProductId=" + id); }
                catch (Exception ex) { AppHost.Toasts.Show("Impossible d'ouvrir le Microsoft Store : " + ex.Message, ToastKind.Error); }
            };
            buttons.Children.Add(store);
        }

        var dock = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);
        if (buttons.Children.Count > 0)
        {
            DockPanel.SetDock(buttons, Dock.Right);
            dock.Children.Add(buttons);
        }
        dock.Children.Add(text);
        return dock;
    }

    // ================================================================== Transparence

    private void RenderTransparency()
    {
        var reg = AppHost.Registry;
        var tweaks = reg.Tweaks.ToList();
        var actions = reg.Actions.ToList();
        var admin = tweaks.Count(t => t.RequiresAdmin) + actions.Count(a => a.RequiresAdmin);
        var unavailable = tweaks.Count(t => AppHost.Engine.Unavailability(t) is not null);

        var metrics = DashUi.Columns(
        [
            Metric(tweaks.Count, "réglages déclarés", "presque tous annulables depuis le journal"),
            Metric(actions.Count, "actions paramétrées", "validées avant exécution"),
            Metric(admin, "demandent l'administrateur", "une seule invite UAC par session"),
            Metric(unavailable, "indisponibles sur ce PC", AppHost.Profile.HardwareLoaded
                ? "édition, matériel ou version de Windows" : "matériel en cours d'analyse"),
        ], 4, 16);

        var divider = new Border { Height = 1, Margin = new Thickness(0, 16, 0, 14) };
        divider.SetResourceReference(Border.BackgroundProperty, "Pp.Divider");

        var privacy = new DockPanel();
        var lockIcon = DashUi.IconBox("", 36, 17, "Pp.Success", "Pp.SuccessBackground", 18);
        lockIcon.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(lockIcon, Dock.Left);
        privacy.Children.Add(lockIcon);

        var links = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        if (reg.GetPage("transparency") is not null)
        {
            var b = DashUi.Button("Détails", null, "Pp.Button");
            b.ToolTip = "Ouvrir la page Transparence : tout ce que Timonier peut modifier et pourquoi";
            b.Click += (_, _) => AppHost.Navigator.Navigate("transparency");
            links.Children.Add(b);
        }
        if (reg.GetPage("journal") is not null)
        {
            var b = DashUi.Button("Journal", "", "Pp.SubtleButton");
            b.Margin = new Thickness(6, 0, 0, 0);
            b.ToolTip = "Historique des modifications, avec annulation";
            b.Click += (_, _) => AppHost.Navigator.Navigate("journal");
            links.Children.Add(b);
        }
        if (links.Children.Count > 0)
        {
            DockPanel.SetDock(links, Dock.Right);
            privacy.Children.Add(links);
        }
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(DashUi.Text("Timonier fonctionne 100 % en local, sans télémétrie", "Pp.Body", 14, semiBold: true));
        text.Children.Add(DashUi.Text("Aucune donnée ne quitte ce PC. Chaque modification est journalisée et, sauf exception signalée, annulable ; les opérations " +
                                      "administrateur passent par un processus élevé qui revalide tout.", "Pp.Caption", 12.5));
        privacy.Children.Add(text);

        var stack = new StackPanel();
        stack.Children.Add(metrics);
        stack.Children.Add(divider);
        stack.Children.Add(privacy);
        _transparencyHost.Content = DashUi.Card(stack, new Thickness(20, 16, 20, 16));
    }

    private static FrameworkElement Metric(int value, string label, string detail)
    {
        var s = new StackPanel();
        s.Children.Add(DashUi.Text(value.ToString("N0", Fr), "Pp.Metric", 26));
        s.Children.Add(DashUi.Text(label, "Pp.Body", 13, semiBold: true));
        var d = DashUi.Text(detail, "Pp.Caption", 12);
        d.Margin = new Thickness(0, 1, 0, 0);
        s.Children.Add(d);
        return s;
    }
}

/// <summary>Tuile de mesure en direct : icône, titre, valeur, détail et anneau ou barre facultatifs.</summary>
internal sealed class LiveTile : Border
{
    private readonly TextBlock _value;
    private readonly TextBlock _detail;
    private readonly TextBlock _icon;
    private readonly DashRing? _ring;
    private readonly DashBar? _bar;
    private string? _fill;

    public LiveTile(string glyph, string title, bool ring = false, bool bar = false)
    {
        SetResourceReference(StyleProperty, "Pp.Card");
        Padding = new Thickness(16, 14, 16, 14);

        _icon = DashUi.Icon(glyph, 14, "Pp.TextSecondary");
        _icon.Margin = new Thickness(0, 0, 8, 0);
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(_icon);
        head.Children.Add(DashUi.Text(title, "Pp.Caption", 12.5));

        _value = DashUi.Text("…", "Pp.Metric", 22);
        _value.TextTrimming = TextTrimming.CharacterEllipsis;
        _detail = DashUi.Text("", "Pp.Caption", 12);
        _detail.Margin = new Thickness(0, 2, 0, 0);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(_value);
        text.Children.Add(_detail);

        var body = new DockPanel();
        if (ring)
        {
            _ring = new DashRing { Width = 52, Height = 52, StrokeThickness = 6, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(_ring, Dock.Right);
            body.Children.Add(_ring);
        }
        body.Children.Add(text);

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(body);
        if (bar)
        {
            _bar = new DashBar(6) { Margin = new Thickness(0, 12, 0, 0) };
            stack.Children.Add(_bar);
        }
        Child = stack;
    }

    public void Set(string value, string detail, double? ratio, string fillKey, string? glyphOverride = null)
    {
        _value.Text = value;
        _detail.Text = detail;
        if (_fill != fillKey)
        {
            _fill = fillKey;
            _ring?.SetResourceReference(DashRing.FillProperty, fillKey);
            if (_bar is not null) _bar.FillKey = fillKey;
            if (_ring is null && _bar is null) _value.SetResourceReference(TextBlock.ForegroundProperty, fillKey == "Pp.Accent" ? "Pp.TextPrimary" : fillKey);
        }
        if (ratio is { } r)
        {
            if (_ring is not null) _ring.Value = r;
            if (_bar is not null) _bar.Value = r;
        }
        if (glyphOverride is not null)
        {
            _icon.Text = glyphOverride;
            _icon.SetResourceReference(TextBlock.ForegroundProperty, fillKey);
        }
    }
}
