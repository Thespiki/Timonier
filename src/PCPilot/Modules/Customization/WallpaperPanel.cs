using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using PcPilot.Core.Engine;
using PcPilot.Core.Platform;
using PcPilot.Core.Settings;
using PcPilot.UI.Services;

namespace PcPilot.Modules.Customization;

/// <summary>
/// Carte « Fond d'écran » : aperçu du fond actuel, choix d'une image (position incluse) ou d'une couleur unie,
/// et retour immédiat au fond précédent. Toutes les modifications passent par les actions journalisées du module.
/// </summary>
internal sealed class WallpaperPanel : UserControl
{
    private const string PreviousKey = "custom.wallpaper.previous";
    private const int PreviewDecodeWidth = 520;

    /// <summary>Couleurs unies proposées (contenu choisi par l'utilisateur, pas un style d'interface).</summary>
    private static readonly (string Hex, string Name)[] SolidColors =
    [
        ("000000", "Noir"), ("4C4A48", "Anthracite"), ("767676", "Gris"), ("0063B1", "Bleu nuit"), ("0078D4", "Bleu"),
        ("2D7D9A", "Bleu canard"), ("038387", "Sarcelle"), ("107C10", "Vert"), ("744DA9", "Violet"),
        ("881798", "Prune"), ("C30052", "Framboise"), ("D13438", "Rouge"), ("CA5010", "Orange brûlé"), ("847545", "Bronze"),
    ];

    private readonly Image _preview = new() { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _previewColor = new();
    private readonly StackPanel _placeholder;
    private readonly TextBlock _current;
    private readonly ComboBox _position;
    private readonly Button _pick, _other, _restore;
    private readonly WrapPanel _swatches = new();
    private readonly ProgressBar _busy = UiKit.BusyBar();
    private WallpaperState? _state;
    private string? _decodedKey;
    private bool _suppressPosition, _isBusy;

    /// <summary>Déclenché avec la miniature du fond actuel (null si couleur unie ou aperçu indisponible).</summary>
    public event EventHandler<ImageSource?>? PreviewChanged;

    public WallpaperPanel()
    {
        Focusable = false;

        // ---- Aperçu (16:9)
        _placeholder = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        _placeholder.Children.Add(UiKit.Icon("", 22, "Pp.TextTertiary").Also(i => i.HorizontalAlignment = HorizontalAlignment.Center));
        _placeholder.Children.Add(UiKit.Text("Aperçu indisponible", "Pp.Caption").Also(t => { t.Margin = new Thickness(0, 6, 0, 0); t.HorizontalAlignment = HorizontalAlignment.Center; }));
        var previewGrid = new Grid();
        previewGrid.Children.Add(_previewColor);
        previewGrid.Children.Add(_preview);
        previewGrid.Children.Add(_placeholder);
        UiKit.ClipRounded(previewGrid, 5);
        var previewFrame = new Border
        {
            Width = 256, Height = 144, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top, Child = previewGrid,
        }.Themed(Border.BorderBrushProperty, "Pp.CardBorder").Themed(Border.BackgroundProperty, "Pp.CardSecondary");

        // ---- Détails
        var details = new StackPanel { Margin = new Thickness(20, 0, 0, 0) };
        var header = new DockPanel();
        DockPanel.SetDock(_busy, Dock.Right);
        header.Children.Add(_busy);
        header.Children.Add(UiKit.Text("Fond d'écran", "Pp.CardTitle").Also(t => t.FontWeight = FontWeights.SemiBold));
        details.Children.Add(header);
        _current = UiKit.Text("Lecture…", "Pp.Caption");
        _current.TextTrimming = TextTrimming.CharacterEllipsis;
        _current.TextWrapping = TextWrapping.NoWrap;
        _current.Margin = new Thickness(0, 2, 0, 0);
        details.Children.Add(_current);

        var row = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        _pick = UiKit.Button("Choisir une image…", "", "Pp.AccentButton", async (_, _) => await PickImageAsync());
        _pick.Margin = new Thickness(0, 0, 16, 6);
        row.Children.Add(_pick);
        var positionLabel = UiKit.Text("Position", "Pp.Body");
        positionLabel.VerticalAlignment = VerticalAlignment.Center;
        positionLabel.Margin = new Thickness(0, 0, 8, 6);
        row.Children.Add(positionLabel);
        _position = new ComboBox { Width = 220, Margin = new Thickness(0, 0, 0, 6), DisplayMemberPath = nameof(WallpaperPosition.Label), ItemsSource = WallpaperPosition.All };
        System.Windows.Automation.AutomationProperties.SetName(_position, "Position du fond d'écran");
        _position.SelectionChanged += async (_, _) => { if (!_suppressPosition) await ApplyPositionAsync(); };
        row.Children.Add(_position);
        details.Children.Add(row);

        details.Children.Add(UiKit.Text("Ou une couleur unie :", "Pp.Caption").Also(t => t.Margin = new Thickness(0, 8, 0, 6)));
        foreach (var (hex, name) in SolidColors) _swatches.Children.Add(Swatch(hex, name));
        var plus = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1),
            Child = UiKit.Icon("", 12).Also(i => i.HorizontalAlignment = HorizontalAlignment.Center),
        }.Themed(Border.BorderBrushProperty, "Pp.ControlStroke").Themed(Border.BackgroundProperty, "Pp.ControlFill");
        _other = new Button { Content = plus, Padding = new Thickness(2), MinHeight = 0, Margin = new Thickness(0, 0, 2, 4) }.Styled("Pp.SubtleButton");
        _other.ToolTip = "Autre couleur (code hexadécimal RRVVBB)…";
        System.Windows.Automation.AutomationProperties.SetName(_other, "Autre couleur unie");
        _other.Click += async (_, _) => await PickCustomColorAsync();
        _swatches.Children.Add(_other);
        details.Children.Add(_swatches);

        var links = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        _restore = UiKit.Button("Restaurer le fond précédent", "", "Pp.LinkButton", async (_, _) => await RestorePreviousAsync());
        _restore.Margin = new Thickness(-2, 0, 16, 0);
        links.Children.Add(_restore);
        var settings = UiKit.Button("Paramètres Windows : arrière-plan", "", "Pp.LinkButton", (_, _) => AppearancePanel.OpenSettings("ms-settings:personalization-background"));
        settings.Margin = new Thickness(-2, 0, 0, 0);
        links.Children.Add(settings);
        details.Children.Add(links);

        details.Children.Add(UiKit.Text("Si un diaporama ou « Windows à la une » est actif dans les Paramètres, il peut remplacer l'image choisie.", "Pp.Caption")
            .Themed(TextBlock.ForegroundProperty, "Pp.TextTertiary").Also(t => t.Margin = new Thickness(0, 8, 0, 0)));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(previewFrame);
        Grid.SetColumn(details, 1);
        grid.Children.Add(details);
        Content = new Border { Child = grid, Padding = new Thickness(16) }.Styled("Pp.Card");

        UpdateRestoreVisibility();
        Loaded += async (_, _) =>
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            await RefreshAsync();
        };
        Unloaded += (_, _) => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private Button Swatch(string hex, string name)
    {
        var chip = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1),
            Background = UiKit.BrushFromHex(hex),
        }.Themed(Border.BorderBrushProperty, "Pp.ControlStroke");
        var b = new Button { Content = chip, Padding = new Thickness(2), MinHeight = 0, Margin = new Thickness(0, 0, 2, 4), ToolTip = $"{name} (#{hex})" }
            .Styled("Pp.SubtleButton");
        System.Windows.Automation.AutomationProperties.SetName(b, "Couleur unie : " + name);
        b.Click += async (_, _) => await ApplyColorAsync(hex);
        return b;
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        // Fond d'écran modifié ailleurs (Paramètres, autre application) pendant que la page est visible.
        if (e.Category == UserPreferenceCategory.Desktop) Dispatcher.InvokeAsync(async () => await RefreshAsync());
    }

    // ================================================================== Lecture de l'état

    public async Task RefreshAsync()
    {
        try
        {
            var previousKey = _decodedKey;
            var (state, image, key) = await Task.Run(() =>
            {
                var s = WallpaperState.Read();
                if (s.IsSolidColor) return (s, (ImageSource?)null, "color");
                var source = File.Exists(s.FilePath) ? s.FilePath : TranscodedWallpaperPath();
                var k = source + "|" + SafeLastWrite(source).Ticks;
                if (k == previousKey) return (s, null, k); // déjà décodé
                return (s, UiKit.LoadThumbnail(source, PreviewDecodeWidth), k);
            });
            _state = state;
            if (key != previousKey || state.IsSolidColor)
            {
                _decodedKey = key;
                _preview.Source = image;
                PreviewChanged?.Invoke(this, image);
            }
            UpdateView();
        }
        catch (Exception ex)
        {
            Log.Error("Customization", "lecture du fond d'écran", ex);
            _current.Text = "Impossible de lire le fond d'écran actuel.";
        }
    }

    private void UpdateView()
    {
        var s = _state;
        if (s is null) return;
        if (s.IsSolidColor)
        {
            _current.Text = s.BackgroundHex is { } hex ? $"Couleur unie (#{hex})" : "Couleur unie";
            _current.ToolTip = null;
            _previewColor.Background = UiKit.BrushFromHex(s.BackgroundHex);
            _preview.Visibility = Visibility.Collapsed;
            _placeholder.Visibility = _previewColor.Background is null ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            var exists = File.Exists(s.FilePath);
            _current.Text = exists
                ? $"{Path.GetFileName(s.FilePath)} · {Path.GetDirectoryName(s.FilePath)}"
                : "Image d'origine introuvable (copie conservée par Windows)";
            _current.ToolTip = s.FilePath;
            _previewColor.Background = UiKit.BrushFromHex(s.BackgroundHex);
            _preview.Visibility = Visibility.Visible;
            _preview.Stretch = s.Position?.Key switch
            {
                "fit" or "center" or "tile" => Stretch.Uniform,
                "stretch" => Stretch.Fill,
                _ => Stretch.UniformToFill,
            };
            _placeholder.Visibility = _preview.Source is null ? Visibility.Visible : Visibility.Collapsed;
        }

        _suppressPosition = true;
        _position.SelectedItem = s.Position ?? WallpaperPosition.Get("fill");
        _suppressPosition = false;
        UpdateRestoreVisibility();
    }

    private static string TranscodedWallpaperPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");

    private static DateTime SafeLastWrite(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return DateTime.MinValue; }
    }

    // ================================================================== Actions

    private async Task PickImageAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choisir un fond d'écran",
            Filter = "Images (JPEG, PNG, BMP, GIF, TIFF)|" + string.Join(";", CustomizationValidate.WallpaperExtensions.Select(e => "*" + e)),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var position = _position.SelectedItem as WallpaperPosition ?? WallpaperPosition.Get("fill")!;
        await ApplyImageAsync(dialog.FileName, position);
    }

    private async Task ApplyPositionAsync()
    {
        if (_position.SelectedItem is not WallpaperPosition position || _state is not { } s) return;
        if (s.IsSolidColor || !IsUsableImage(s.FilePath))
        {
            AppHost.Toasts.Show("La position sera utilisée pour la prochaine image choisie.", ToastKind.Info);
            return;
        }
        if (s.Position?.Key == position.Key) return;
        await ApplyImageAsync(s.FilePath, position);
    }

    private static bool IsUsableImage(string path) =>
        path.Length > 0 && File.Exists(path) && CustomizationValidate.WallpaperExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private Task ApplyImageAsync(string path, WallpaperPosition position) =>
        RunAsync(SetWallpaperAction.ActionId, new Dictionary<string, string> { ["path"] = path, ["position"] = position.Key });

    private Task ApplyColorAsync(string hex) =>
        RunAsync(SetSolidColorAction.ActionId, new Dictionary<string, string> { ["color"] = hex });

    private async Task PickCustomColorAsync()
    {
        var value = await AppHost.Dialogs.PromptAsync("Couleur unie",
            "Code hexadécimal de la couleur (RRVVBB), par exemple 1E3A5F pour un bleu profond :",
            _state?.BackgroundHex ?? "",
            validate: v => System.Text.RegularExpressions.Regex.IsMatch(v.Trim().TrimStart('#'), "^[0-9A-Fa-f]{6}$")
                ? null
                : "Six caractères hexadécimaux attendus (0-9, A-F).");
        if (value is null) return;
        await ApplyColorAsync(value.Trim().TrimStart('#').ToUpperInvariant());
    }

    private async Task RunAsync(string actionId, Dictionary<string, string> parameters)
    {
        if (_isBusy) return;
        SetBusy(true);
        ApplyOutcome outcome;
        try
        {
            outcome = await AppHost.Engine.RunActionAsync(actionId, parameters);
        }
        finally
        {
            SetBusy(false);
        }

        if (!outcome.Success)
        {
            AppHost.Toasts.ShowOutcome(outcome);
            return;
        }
        if (outcome.Data is { } data) SavePrevious(data);
        // « Annuler » réapplique immédiatement le fond précédent (l'annulation du journal ne restaure que le registre).
        AppHost.Toasts.Show(outcome.Message, ToastKind.Success, HasPrevious() ? "Annuler" : null,
            HasPrevious() ? () => _ = RestorePreviousAsync() : null);
        await RefreshAsync();
    }

    private async Task RestorePreviousAsync()
    {
        var previous = LoadPrevious();
        if (previous is null)
        {
            AppHost.Toasts.Show("Aucun fond d'écran précédent n'est mémorisé.", ToastKind.Info);
            return;
        }
        var path = previous.GetValueOrDefault("previousPath") ?? "";
        if (path.Length == 0 && previous.GetValueOrDefault("previousColor") is { } color)
        {
            await ApplyColorAsync(color);
            return;
        }
        if (!IsUsableImage(path))
        {
            AppHost.Toasts.Show("Le fond d'écran précédent n'est plus disponible sous forme d'image (fichier déplacé, supprimé ou géré par Windows).", ToastKind.Warning);
            return;
        }
        var position = WallpaperPosition.Get(previous.GetValueOrDefault("previousPosition") ?? "") ?? WallpaperPosition.Get("fill")!;
        await ApplyImageAsync(path, position);
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        _busy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _pick.IsEnabled = _position.IsEnabled = _other.IsEnabled = _swatches.IsEnabled = !busy;
        _restore.IsEnabled = !busy;
    }

    // ================================================================== Fond précédent (préférences de PC Pilot)

    private static Dictionary<string, string>? LoadPrevious()
    {
        if (!AppHost.Settings.ModuleData.TryGetValue(PreviousKey, out var json) || string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize(json, CoreJson.Default.DictionaryStringString); }
        catch (JsonException) { return null; }
    }

    private static bool HasPrevious() => LoadPrevious() is { } p &&
        (p.GetValueOrDefault("previousPath") is { Length: > 0 } || p.ContainsKey("previousColor"));

    private void SavePrevious(Dictionary<string, string> data)
    {
        var keep = data.Where(kv => kv.Key is "previousPath" or "previousPosition" or "previousColor")
                       .ToDictionary(kv => kv.Key, kv => kv.Value);
        AppHost.Settings.ModuleData[PreviousKey] = JsonSerializer.Serialize(keep, CoreJson.Default.DictionaryStringString);
        SettingsStore.Save();
        UpdateRestoreVisibility();
    }

    private void UpdateRestoreVisibility() =>
        _restore.Visibility = HasPrevious() ? Visibility.Visible : Visibility.Collapsed;
}
