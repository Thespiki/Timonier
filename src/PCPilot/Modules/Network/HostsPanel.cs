using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PcPilot.Core.Platform;
using PcPilot.Core.Security;
using PcPilot.UI.Services;

namespace PcPilot.Modules.Network;

/// <summary>
/// Onglet « Fichier hosts » : blocage de sites dans une section réservée à PC Pilot, liste des autres entrées
/// (lecture seule) avec signalement des redirections. Lecture du fichier hors du thread UI.
/// </summary>
internal sealed class HostsPanel : UserControl
{
    private const int MaxOtherRows = 150;

    private readonly TextBox _hostBox = new() { MinWidth = 300 };
    private readonly CheckBox _www = new() { Content = "Bloquer aussi la variante www.", IsChecked = true, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _error = NetUi.Text("", "Pp.Caption", new Thickness(0, 6, 0, 0));
    private readonly Button _add;
    private readonly ProgressBar _busy = NetUi.Busy();
    private readonly StackPanel _managedHost = new();
    private readonly StackPanel _othersHost = new();
    private readonly TextBlock _backupText = NetUi.Text("", "Pp.Caption");
    private readonly List<UIElement> _actionControls = [];
    private bool _loading;

    public HostsPanel()
    {
        Focusable = false;
        var root = new StackPanel();

        root.Children.Add(NetUi.InfoBar(
            "Le fichier hosts associe des noms de sites à des adresses, avant même toute requête DNS. Pour bloquer un site sur tout le PC " +
            "(tous les navigateurs et applications, tous les comptes), PC Pilot y ajoute des lignes « 0.0.0.0 site » dans sa propre section, " +
            "sans jamais modifier le reste du fichier. Un VPN ou le DNS sécurisé d'un navigateur ne contournent pas ce blocage. Limites : " +
            "les sous-domaines ne sont pas couverts (m.site.com, video.site.com) et une application qui contacte directement une adresse IP n'est pas bloquée.",
            "", "Pp.InfoBar"));

        // --- Ajout
        var addCard = new StackPanel();
        addCard.Children.Add(new TextBlock { Text = "Bloquer un site", FontWeight = FontWeights.SemiBold }.Styled("Pp.CardTitle"));
        addCard.Children.Add(NetUi.Text("Saisissez un nom de domaine (ex. exemple.com) ou collez l'adresse d'une page.", "Pp.Caption", new Thickness(0, 2, 0, 10)));
        System.Windows.Automation.AutomationProperties.SetName(_hostBox, "Site à bloquer");
        _hostBox.TextChanged += (_, _) => ValidateInput();
        _hostBox.KeyDown += async (_, e) => { if (e.Key == Key.Enter && _add!.IsEnabled) await AddAsync(); };
        _add = NetUi.Button("Bloquer", "", "Pp.AccentButton", async (_, _) => await AddAsync());
        _add.Margin = new Thickness(10, 0, 0, 0);
        _add.IsEnabled = false;
        var addRow = new DockPanel();
        var addButtons = NetUi.Row(_add, _busy);
        DockPanel.SetDock(addButtons, Dock.Right);
        addRow.Children.Add(addButtons);
        addRow.Children.Add(_hostBox);
        addCard.Children.Add(addRow);
        addCard.Children.Add(_www);
        addCard.Children.Add(_error);
        root.Children.Add(NetUi.Card(addCard, new Thickness(0, 0, 0, 8)));
        _actionControls.AddRange([_add, _hostBox, _www]);

        root.Children.Add(_managedHost);
        root.Children.Add(_othersHost);

        root.Children.Add(NetUi.InfoBar(
            "Microsoft Defender peut signaler une modification massive du fichier hosts (surtout si elle vise des domaines Microsoft) : " +
            "c'est une technique souvent employée par les logiciels malveillants. PC Pilot limite sa section à " + HostsFile.MaxManagedEntries +
            " entrées et refuse de bloquer les domaines de Windows Update, Defender et SmartScreen.",
            "", "Pp.InfoBar.Warning"));

        var footer = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var openFolder = NetUi.Button("Ouvrir le dossier du fichier", "", "Pp.Button", (_, _) => OpenFolder());
        DockPanel.SetDock(openFolder, Dock.Right);
        footer.Children.Add(openFolder);
        _backupText.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(_backupText);
        root.Children.Add(footer);

        Content = root;
        Loaded += async (_, _) => await ReloadAsync();
        ValidateInput();
    }

    private string? ValidatedHost()
    {
        try { return HostsFile.ValidateBlockHost(_hostBox.Text); }
        catch (ValidationException) { return null; }
    }

    private void ValidateInput()
    {
        var text = _hostBox.Text.Trim();
        string? error = null;
        if (text.Length > 0)
        {
            try { HostsFile.ValidateBlockHost(text); }
            catch (ValidationException ex) { error = ex.Message; }
        }
        _error.Text = error ?? "";
        _error.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        _error.SetResourceReference(TextBlock.ForegroundProperty, "Pp.Danger");
        _add.IsEnabled = !_loading && error is null && text.Length > 0;
    }

    private async Task ReloadAsync()
    {
        _managedHost.Children.Clear();
        _managedHost.Children.Add(NetUi.EmptyState("", "Lecture du fichier hosts…"));
        var snap = await Task.Run(HostsFile.Read);
        Render(snap);
    }

    private void Render(HostsSnapshot snap)
    {
        _actionControls.RemoveAll(c => c is Button b && b != _add);
        _managedHost.Children.Clear();
        _othersHost.Children.Clear();
        _backupText.Text = snap.BackupTime is { } t
            ? $"Sauvegarde automatique : hosts.pcpilot.bak ({Format.Date(t)})"
            : "Une sauvegarde (hosts.pcpilot.bak) est créée avant chaque modification.";

        if (snap.Error is not null)
        {
            _managedHost.Children.Add(NetUi.EmptyState("", "Fichier hosts illisible", snap.Error));
            return;
        }

        // --- Section PC Pilot
        var managed = snap.Managed.Select(e => e.Host).Distinct().ToList();
        var header = new DockPanel { Margin = new Thickness(2, 22, 0, 8) };
        if (managed.Count > 0)
        {
            var clear = NetUi.Button("Tout débloquer", "", "Pp.DangerButton", async (_, _) => await ClearAsync(managed.Count));
            DockPanel.SetDock(clear, Dock.Right);
            header.Children.Add(clear);
            _actionControls.Add(clear);
        }
        var title = new TextBlock { Text = $"Sites bloqués par PC Pilot ({managed.Count} / {HostsFile.MaxManagedEntries})", VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.SectionTitle");
        title.Margin = new Thickness(0);
        header.Children.Add(title);
        _managedHost.Children.Add(header);

        if (managed.Count == 0)
        {
            _managedHost.Children.Add(NetUi.EmptyState("", "Aucun site bloqué", "Les sites que vous bloquez ici apparaîtront dans cette liste."));
        }
        else
        {
            var list = new StackPanel();
            for (var i = 0; i < managed.Count; i++)
            {
                var host = managed[i];
                if (i > 0) list.Children.Add(NetUi.Divider(new Thickness(0, 4, 0, 4)));
                var row = new DockPanel();
                var remove = NetUi.Button("Débloquer", null, "Pp.SubtleButton", async (_, _) => await RemoveAsync(host));
                DockPanel.SetDock(remove, Dock.Right);
                _actionControls.Add(remove);
                row.Children.Add(remove);
                var icon = NetUi.Icon("", 14, "Pp.Danger");
                icon.Margin = new Thickness(0, 0, 10, 0);
                row.Children.Add(NetUi.Row(icon, new TextBlock { Text = host, VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.Body")));
                list.Children.Add(row);
            }
            _managedHost.Children.Add(NetUi.Card(list));
        }

        // --- Autres entrées (lecture seule)
        var others = snap.Others.Where(e => !(e.Host is "localhost" && e.IsBlock)).ToList();
        var redirects = others.Where(e => !e.IsBlock).ToList();
        _othersHost.Children.Add(NetUi.Section($"Autres entrées du fichier ({others.Count})"));
        if (redirects.Count > 0)
        {
            _othersHost.Children.Add(NetUi.InfoBar(
                $"{redirects.Count} entrée(s) redirigent un nom vers une autre adresse. C'est normal pour un serveur local, un logiciel de développement ou " +
                "certains outils d'entreprise ; si vous ne les avez pas ajoutées, cela peut signaler un logiciel indésirable qui détourne des sites.",
                "", "Pp.InfoBar.Warning"));
        }
        if (others.Count == 0)
        {
            _othersHost.Children.Add(NetUi.Text("Aucune autre entrée active : le fichier ne contient que des commentaires (état d'origine de Windows).", "Pp.Caption", new Thickness(2, 0, 0, 8)));
            return;
        }
        var otherList = new StackPanel();
        foreach (var e in others.OrderBy(e => e.IsBlock).Take(MaxOtherRows))
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(NetUi.Text(e.Address, "Pp.Caption"));
            var h = NetUi.Text(e.Host);
            Grid.SetColumn(h, 1);
            g.Children.Add(h);
            var badge = e.IsBlock ? NetUi.Badge("Blocage") : NetUi.Badge("Redirection", "Warning");
            Grid.SetColumn(badge, 2);
            g.Children.Add(badge);
            otherList.Children.Add(g);
        }
        if (others.Count > MaxOtherRows)
            otherList.Children.Add(NetUi.Text($"… et {others.Count - MaxOtherRows} autre(s) entrée(s).", "Pp.Caption", new Thickness(0, 6, 0, 0)));
        otherList.Children.Add(NetUi.Text("Ces lignes n'ont pas été ajoutées par PC Pilot : elles ne sont jamais modifiées.", "Pp.Caption", new Thickness(0, 8, 0, 0)));
        _othersHost.Children.Add(NetUi.Card(otherList));
    }

    private async Task AddAsync()
    {
        var host = ValidatedHost();
        if (host is null) return;
        var outcome = await RunAsync(NetworkActionIds.HostsAdd, new() { ["host"] = host, ["www"] = _www.IsChecked == true ? "true" : "false" });
        if (outcome) _hostBox.Text = "";
    }

    private Task<bool> RemoveAsync(string host) => RunAsync(NetworkActionIds.HostsRemove, new() { ["host"] = host });

    private async Task ClearAsync(int count)
    {
        if (!await AppHost.Dialogs.ConfirmAsync("Débloquer tous les sites",
                $"Les {count} entrée(s) ajoutées par PC Pilot vont être retirées du fichier hosts. Les autres lignes du fichier ne sont pas modifiées.",
                "Tout débloquer", danger: true))
            return;
        await RunAsync(NetworkActionIds.HostsClear, []);
    }

    private async Task<bool> RunAsync(string actionId, Dictionary<string, string> parameters)
    {
        _loading = true;
        var outcome = await NetworkUiActions.RunAsync(actionId, parameters, [.. _actionControls], _busy, showOutcome: false);
        _loading = false;
        if (outcome.Success) AppHost.Toasts.Show(outcome.Message, ToastKind.Success);
        else if (!outcome.Cancelled) AppHost.Toasts.ShowOutcome(outcome);
        await ReloadAsync();
        ValidateInput();
        return outcome.Success;
    }

    private static void OpenFolder()
    {
        try { ProcessRunner.OpenFolder(Path.GetDirectoryName(HostsFile.FilePath)!); }
        catch (Exception ex)
        {
            Log.Warn("Network", "ouverture du dossier etc : " + ex.Message);
            AppHost.Toasts.Show("Impossible d'ouvrir le dossier.", ToastKind.Error);
        }
    }
}
