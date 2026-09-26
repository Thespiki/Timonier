using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Timonier.Core.Model;
using Timonier.Core.Platform;
using Timonier.UI.Controls;
using Timonier.UI.Services;

namespace Timonier.Modules.Customization;

/// <summary>
/// Page « Personnalisation » : apparence (mode clair/sombre, accent), fond d'écran et écran de verrouillage,
/// puis tous les réglages de la catégorie (barre des tâches, Démarrer, Explorateur, bureau, connexion, saisie).
/// Paramètres de navigation reconnus : <c>tweak:&lt;id&gt;</c> et <c>section:appearance|wallpaper|lockscreen|&lt;groupe&gt;</c>.
/// </summary>
public sealed class CustomizationPage : UserControl, INavigationAware
{
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _stack;
    private readonly TweakListView _appearanceList;
    private readonly TweakListView _list;
    private readonly FrameworkElement _appearanceAnchor, _wallpaperAnchor, _lockAnchor;

    // Réglages absents de cette version de Windows : regroupés à la fin, liste créée à la première ouverture.
    private readonly List<TweakDefinition> _unavailable;
    private TweakListView? _unavailableList;
    private Button? _unavailableToggle;
    private TextBlock? _unavailableText, _unavailableChevron;
    private string _unavailableLabel = "";

    public CustomizationPage()
    {
        _stack = new StackPanel().Styled("Pp.PageStack");
        _scroll = new ScrollViewer { Content = _stack }.Styled("Pp.PageScroll");
        Content = _scroll;

        _stack.Children.Add(new PageHeader
        {
            Title = L("Personnalisation"),
            Subtitle = L("Mode clair ou sombre, couleurs, fond d'écran, barre des tâches, menu Démarrer, Explorateur et ouverture de session."),
            Glyph = "",
        });

        var jumpBar = new WrapPanel { Margin = new Thickness(0, -4, 0, 0) };
        _stack.Children.Add(jumpBar);

        // ---- Apparence
        _appearanceAnchor = PageScaffold.Section(L("Apparence"));
        _stack.Children.Add(_appearanceAnchor);
        var appearance = new AppearancePanel();
        _stack.Children.Add(appearance);
        var allTweaks = AppHost.Registry.TweaksIn(CustomizationModule.Category).ToList();
        _appearanceList = new TweakListView(allTweaks.Where(t => CustomizationTweaks.AppearanceIds.Contains(t.Id)), showRecommendations: false);
        _stack.Children.Add(_appearanceList);

        // ---- Fond d'écran et écran de verrouillage
        _wallpaperAnchor = PageScaffold.Section(L("Fond d'écran et écran de verrouillage"));
        _stack.Children.Add(_wallpaperAnchor);
        var wallpaper = new WallpaperPanel();
        wallpaper.PreviewChanged += (_, image) => appearance.SetDesktopPreview(image);
        _stack.Children.Add(wallpaper);
        var lockScreen = new LockScreenPanel { Margin = new Thickness(0, 8, 0, 0) };
        _lockAnchor = lockScreen;
        _stack.Children.Add(lockScreen);

        // ---- Réglages détaillés
        _stack.Children.Add(ExplorerRestartBar());
        var others = allTweaks.Where(t => !CustomizationTweaks.AppearanceIds.Contains(t.Id)).ToList();
        _unavailable = [.. others.Where(t => AppHost.Engine.Unavailability(t) is not null)];
        _list = new TweakListView(others.Except(_unavailable));
        _stack.Children.Add(_list);
        if (_unavailable.Count > 0) AddUnavailableToggle();

        BuildJumpBar(jumpBar);
    }

    // ================================================================== Navigation interne

    private void BuildJumpBar(WrapPanel bar)
    {
        AddJump(bar, L("Fond d'écran"), "", () => _wallpaperAnchor);
        foreach (var (group, label, glyph) in new[]
                 {
                     (CustomizationTweaks.GroupTaskbar, L("Barre des tâches"), ""),
                     (CustomizationTweaks.GroupStart, L("Démarrer"), ""),
                     (CustomizationTweaks.GroupExplorer, L("Explorateur"), ""),
                     (CustomizationTweaks.GroupDesktop, L("Bureau"), ""),
                     (CustomizationTweaks.GroupLogon, L("Connexion"), ""),
                     (CustomizationTweaks.GroupInput, L("Souris et clavier"), ""),
                 })
        {
            if (GroupHeading(group) is null) continue; // groupe vide (réglages avancés masqués)
            AddJump(bar, label, glyph, () => GroupHeading(group));
        }
    }

    private void AddJump(WrapPanel bar, string label, string glyph, Func<FrameworkElement?> target)
    {
        var b = UiKit.Button(label, glyph, "Pp.Button", (_, _) => ScrollTo(target()));
        b.Padding = new Thickness(8, 3, 10, 3);
        b.MinHeight = 28;
        b.FontSize = 12;
        b.Margin = new Thickness(0, 0, 6, 6);
        b.ToolTip = L("Aller à la section « {0} »", label);
        bar.Children.Add(b);
    }

    /// <summary>Titre de groupe généré par la liste de réglages (TextBlock « Pp.SectionTitle »).</summary>
    private FrameworkElement? GroupHeading(string group) =>
        (_list.Content as Panel)?.Children.OfType<TextBlock>().FirstOrDefault(t => t.Text == group);

    private void ScrollTo(FrameworkElement? element)
    {
        if (element is null || !element.IsVisible) return;
        try
        {
            var y = element.TransformToAncestor(_scroll).Transform(new Point(0, 0)).Y + _scroll.VerticalOffset;
            _scroll.ScrollToVerticalOffset(Math.Max(0, y - 12));
        }
        catch (InvalidOperationException)
        {
            element.BringIntoView();
        }
    }

    public void OnNavigatedTo(object? parameter)
    {
        if (parameter is not string s) return;
        if (s.StartsWith("tweak:", StringComparison.Ordinal))
        {
            var id = s["tweak:".Length..];
            if (CustomizationTweaks.AppearanceIds.Contains(id)) _appearanceList.Highlight(id);
            else if (!_list.Highlight(id) && _unavailable.Any(t => t.Id == id))
            {
                ShowUnavailable(true);
                _unavailableList?.Highlight(id);
            }
            return;
        }
        if (!s.StartsWith("section:", StringComparison.Ordinal)) return;
        var section = s["section:".Length..];
        // Après la mise en page (la page vient peut-être d'être créée).
        Dispatcher.InvokeAsync(() => ScrollTo(section switch
        {
            "appearance" => _appearanceAnchor,
            "wallpaper" => _wallpaperAnchor,
            "lockscreen" => _lockAnchor,
            _ => GroupHeading(section),
        }), DispatcherPriority.Loaded);
    }

    // ================================================================== Réglages indisponibles sur ce PC

    private void AddUnavailableToggle()
    {
        _unavailableLabel = LP(_unavailable.Count,
            "{0} réglage n'existe pas sur cette version de Windows",
            "{0} réglages n'existent pas sur cette version de Windows");
        _unavailableText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        _unavailableChevron = UiKit.Icon("", 12, "Pp.AccentText");
        _unavailableChevron.Margin = new Thickness(0, 1, 8, 0);
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(_unavailableChevron);
        content.Children.Add(_unavailableText);
        _unavailableToggle = new Button { Content = content, Margin = new Thickness(-2, 20, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, FontSize = 13 }
            .Styled("Pp.LinkButton");
        _unavailableToggle.ToolTip = L("Réglages réservés à Windows 10 ou à d'autres versions de Windows 11, affichés pour information.");
        _unavailableToggle.Click += (_, _) => ShowUnavailable(_unavailableList is not { Visibility: Visibility.Visible });
        _stack.Children.Add(_unavailableToggle);
        ShowUnavailable(false);
    }

    private void ShowUnavailable(bool show)
    {
        if (_unavailableToggle is null || _unavailableText is null || _unavailableChevron is null) return;
        if (show && _unavailableList is null)
        {
            _unavailableList = new TweakListView(_unavailable, showRecommendations: false);
            _stack.Children.Insert(_stack.Children.IndexOf(_unavailableToggle) + 1, _unavailableList);
        }
        if (_unavailableList is not null) _unavailableList.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _unavailableText.Text = show ? L("{0} : masquer", _unavailableLabel) : L("{0} : afficher", _unavailableLabel);
        _unavailableChevron.Text = show ? "" : "";
    }

    // ================================================================== Redémarrage de l'Explorateur

    private static Border ExplorerRestartBar()
    {
        var restart = UiKit.Button(L("Relancer l'Explorateur"), "", "Pp.Button", async (_, _) =>
        {
            if (!await AppHost.Dialogs.ConfirmAsync(L("Relancer l'Explorateur ?"),
                    L("La barre des tâches et le bureau disparaissent une seconde, et les fenêtres de dossiers ouvertes sont fermées. Vos applications et documents ne sont pas touchés."), L("Relancer")))
                return;
            try
            {
                await SystemEffects.RestartExplorerAsync();
                AppHost.Toasts.Show(L("Explorateur Windows relancé."), ToastKind.Success);
            }
            catch (Exception ex)
            {
                Log.Error("Customization", "relance de l'Explorateur", ex);
                AppHost.Toasts.Show(L("Impossible de relancer l'Explorateur : {0}", ex.Message), ToastKind.Error);
            }
        });
        restart.Margin = new Thickness(12, 0, 0, 0);
        restart.VerticalAlignment = VerticalAlignment.Center;

        var folders = UiKit.Button(L("Options des dossiers"), "", "Pp.Button", (_, _) =>
        {
            try { ProcessRunner.Launch(SystemTool.Control, "folders"); }
            catch (Exception ex) { AppHost.Toasts.Show(L("Impossible d'ouvrir les options des dossiers : {0}", ex.Message), ToastKind.Error); }
        });
        folders.Margin = new Thickness(12, 0, 0, 0);
        folders.VerticalAlignment = VerticalAlignment.Center;
        folders.ToolTip = L("Options de l'Explorateur de fichiers (Panneau de configuration)");

        var icon = UiKit.Icon("", 16, "Pp.AccentText");
        icon.Margin = new Thickness(0, 1, 12, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        var text = UiKit.Text(L("Les réglages marqués « Explorateur » s'affichent après le redémarrage de l'Explorateur Windows ; ceux marqués « Déconnexion », à la prochaine ouverture de session."));
        text.VerticalAlignment = VerticalAlignment.Center;

        var dock = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(restart, Dock.Right);
        DockPanel.SetDock(folders, Dock.Right);
        dock.Children.Add(icon);
        dock.Children.Add(restart);
        dock.Children.Add(folders);
        dock.Children.Add(text);
        return new Border { Child = dock, Margin = new Thickness(0, 22, 0, 4) }.Styled("Pp.InfoBar");
    }
}
