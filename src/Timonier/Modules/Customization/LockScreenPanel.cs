using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Timonier.Core.Platform;
using Timonier.UI.Services;

namespace Timonier.Modules.Customization;

/// <summary>
/// Carte « Écran de verrouillage » : aperçu de l'image actuelle (API WinRT LockScreen, lecture seule) et choix
/// d'une nouvelle image pour la session de l'utilisateur. Repli sur les Paramètres si Windows refuse.
/// </summary>
internal sealed class LockScreenPanel : UserControl
{
    private readonly Image _preview = new() { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _placeholder;
    private readonly TextBlock _placeholderText;
    private readonly Button _pick;
    private readonly ProgressBar _busy = UiKit.BusyBar();
    private bool _loadedOnce, _isBusy;

    public LockScreenPanel()
    {
        Focusable = false;

        _placeholder = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _placeholder.Children.Add(UiKit.Icon("", 22, "Pp.TextTertiary").Also(i => i.HorizontalAlignment = HorizontalAlignment.Center));
        _placeholderText = UiKit.Text(L("Loading…"), "Pp.Caption").Also(t => { t.Margin = new Thickness(0, 6, 0, 0); t.HorizontalAlignment = HorizontalAlignment.Center; t.TextAlignment = TextAlignment.Center; });
        _placeholder.Children.Add(_placeholderText);
        var previewGrid = new Grid();
        previewGrid.Children.Add(_preview);
        previewGrid.Children.Add(_placeholder);
        UiKit.ClipRounded(previewGrid, 5);
        var previewFrame = new Border
        {
            Width = 256, Height = 144, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top, Child = previewGrid,
        }.Themed(Border.BorderBrushProperty, "Pp.CardBorder").Themed(Border.BackgroundProperty, "Pp.CardSecondary");

        var details = new StackPanel { Margin = new Thickness(20, 0, 0, 0) };
        var header = new DockPanel();
        DockPanel.SetDock(_busy, Dock.Right);
        header.Children.Add(_busy);
        header.Children.Add(UiKit.Text(L("Lock screen"), "Pp.CardTitle").Also(t => t.FontWeight = FontWeights.SemiBold));
        details.Children.Add(header);
        details.Children.Add(UiKit.Text(L("Picture shown when your PC is locked (Windows + L)."), "Pp.Caption")
            .Also(t => t.Margin = new Thickness(0, 2, 0, 0)));

        var row = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        _pick = UiKit.Button(L("Choose a picture…"), "", "Pp.Button", async (_, _) => await PickAsync());
        _pick.Margin = new Thickness(0, 0, 12, 6);
        row.Children.Add(_pick);
        var settings = UiKit.Button(L("Lock screen settings"), "", "Pp.LinkButton", (_, _) => AppearancePanel.OpenSettings("ms-settings:lockscreen"));
        settings.Margin = new Thickness(0, 0, 0, 6);
        settings.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(settings);
        details.Children.Add(row);

        details.Children.Add(UiKit.Text(
                L("Replaces “Windows spotlight” if it's on. This change can't be undone automatically: the previous picture is still offered in Settings. Lock screen widgets, status and apps are set in Settings."),
                "Pp.Caption")
            .Themed(TextBlock.ForegroundProperty, "Pp.TextTertiary").Also(t => t.Margin = new Thickness(0, 6, 0, 0)));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(previewFrame);
        Grid.SetColumn(details, 1);
        grid.Children.Add(details);
        Content = new Border { Child = grid, Padding = new Thickness(16) }.Styled("Pp.Card");

        Loaded += async (_, _) =>
        {
            if (_loadedOnce) return;
            _loadedOnce = true;
            await RefreshAsync();
        };
    }

    private async Task RefreshAsync()
    {
        var image = await Task.Run(LoadCurrentImage);
        _preview.Source = image;
        _placeholder.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
        _placeholderText.Text = L("Preview unavailable");
    }

    /// <summary>Image actuelle de l'écran de verrouillage (lecture seule), réduite pour l'aperçu.</summary>
    private static ImageSource? LoadCurrentImage()
    {
        try
        {
            var stream = Windows.System.UserProfile.LockScreen.GetImageStream();
            if (stream is null) return null;
            using (stream)
            using (var s = stream.AsStreamForRead())
            {
                var buffer = new MemoryStream();
                s.CopyTo(buffer);
                buffer.Position = 0;
                return UiKit.Decode(buffer, 520);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Customization", "aperçu de l'écran de verrouillage : " + ex.Message);
            return null;
        }
    }

    private async Task PickAsync()
    {
        if (_isBusy) return;
        var dialog = new OpenFileDialog
        {
            Title = L("Choose the lock screen picture"),
            Filter = L("Images (JPEG, PNG, BMP)") + "|" + string.Join(";", CustomizationValidate.LockScreenExtensions.Select(e => "*" + e)),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        if (!await AppHost.Dialogs.ConfirmAsync(L("Lock screen"),
                L("The lock screen picture will be replaced with “{0}”.\n\nThis change can't be undone automatically; you can switch back to another picture in Settings.", Path.GetFileName(dialog.FileName)),
                L("Apply")))
            return;

        _isBusy = true;
        _pick.IsEnabled = false;
        _busy.Visibility = Visibility.Visible;
        try
        {
            var outcome = await AppHost.Engine.RunActionAsync(SetLockScreenImageAction.ActionId,
                new Dictionary<string, string> { ["path"] = dialog.FileName });
            if (outcome.Success)
            {
                AppHost.Toasts.Show(outcome.Message, ToastKind.Success);
                await RefreshAsync();
            }
            else
            {
                AppHost.Toasts.Show(outcome.Message, ToastKind.Error, L("Open Settings"),
                    () => AppearancePanel.OpenSettings("ms-settings:lockscreen"));
            }
        }
        finally
        {
            _isBusy = false;
            _pick.IsEnabled = true;
            _busy.Visibility = Visibility.Collapsed;
        }
    }
}
