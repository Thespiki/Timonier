using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Timonier.Core.Engine;
using Timonier.Core.Platform;
using Timonier.UI.Services;

namespace Timonier.Modules.Maintenance;

/// <summary>Section « Réparation » : outils officiels, un seul à la fois, avec progression relayée depuis le broker.</summary>
internal sealed partial class RepairPanel
{
    public FrameworkElement Root { get; }

    private sealed record Tool(string Key, string Glyph, string Title, string Description, string Duration, bool Admin, Func<Task> Run);

    private readonly List<Button> _runButtons = [];
    private readonly Border _jobBar;
    private readonly TextBlock _jobTitle = MaintUi.Text("", "Pp.CardTitle");
    private readonly TextBlock _jobStatus = MaintUi.Text("", "Pp.Caption");
    private readonly ProgressBar _jobProgress = new() { Height = 4, Minimum = 0, Maximum = 100, IsIndeterminate = true, Margin = new Thickness(0, 10, 0, 8) };
    private readonly Border _resultBar;
    private readonly TextBlock _resultText, _resultIcon;
    private bool _busy;

    // Pourcentage en fin de ligne, placé après (« 45 % ») ou avant (« %45 ») le nombre selon la culture de l'interface.
    [GeneratedRegex(@"(?:(\d{1,3})\s?%|%\s?(\d{1,3}))\s*$")]
    private static partial Regex PercentRx();

    public RepairPanel()
    {
        var tools = new List<Tool>
        {
            new("sfc", "", L("Vérifier les fichiers système (SFC)"),
                L("Recherche les fichiers de Windows endommagés ou modifiés et les remplace par des copies saines (sfc /scannow). À lancer en premier en cas de comportement anormal."),
                L("10 à 30 min"), true, () => StartSfcAsync()),
            new("dism", "", L("Réparer l'image de Windows (DISM)"),
                L("Répare la réserve de composants dont SFC se sert, en téléchargeant les éléments sains depuis Windows Update (connexion Internet requise). À lancer si SFC n'a pas pu tout réparer."),
                L("10 à 40 min"), true, () => RunActionJobAsync(RepairDismAction.RestoreHealthId, L("Réparation de l'image de Windows"),
                    L("DISM va vérifier et réparer l'image de Windows en téléchargeant les composants nécessaires depuis Windows Update. Cela peut durer jusqu'à 40 minutes."))),
            new("chkdsk", "", L("Analyser le disque système"),
                L("Contrôle le système de fichiers du lecteur système en ligne (chkdsk /scan), sans bloquer le PC ni redémarrer. Windows corrige à chaud ce qui peut l'être et signale le reste."),
                L("2 à 20 min"), true, () => RunActionJobAsync(ChkdskScanAction.ActionId, L("Analyse du disque système"), null)),
            new("componentcleanup", "", L("Nettoyer le magasin de composants"),
                L("Supprime tout de suite les anciennes versions des composants remplacés par des mises à jour (dossier WinSxS) et libère souvent plusieurs centaines de Mo. Les mises à jour déjà installées ne pourront plus être désinstallées."),
                L("5 à 30 min"), true, () => RunActionJobAsync(RepairDismAction.ComponentCleanupId, L("Nettoyage du magasin de composants"),
                    L("Les anciennes versions des composants seront supprimées immédiatement : vous ne pourrez plus désinstaller les mises à jour déjà installées. Continuer ?"))),
            new("iconcache", "", L("Réinitialiser le cache des icônes et des miniatures"),
                L("Corrige les icônes vides, incorrectes ou les aperçus erronés. L'Explorateur est fermé quelques secondes (la barre des tâches et le bureau disparaissent), les caches sont supprimés, puis tout est reconstruit."),
                L("quelques secondes"), false, ResetIconCacheAsync),
            new("wsreset", "", L("Réinitialiser le cache du Microsoft Store"),
                L("Vide le cache du Microsoft Store (WSReset) quand il ne s'ouvre plus ou ne télécharge plus. Le Store s'ouvre à la fin ; vos applications et leurs données ne sont pas touchées."),
                L("moins d'une minute"), false, ResetStoreAsync),
            new("time", "", L("Resynchroniser l'heure"),
                L("Force la synchronisation immédiate de l'horloge avec le serveur de temps de Windows (connexion Internet requise). Utile si l'heure dérive ou après un changement de pile."),
                L("quelques secondes"), true, () => RunActionJobAsync(TimeResyncAction.ActionId, L("Synchronisation de l'heure"), null)),
        };

        var root = new StackPanel();
        _jobProgress.SetResourceReference(Control.ForegroundProperty, "Pp.Accent");
        var jobBody = new StackPanel();
        var jobHead = new DockPanel();
        var spinnerIcon = MaintUi.Icon("", 16, "Pp.AccentText");
        spinnerIcon.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(spinnerIcon, Dock.Left);
        jobHead.Children.Add(spinnerIcon);
        jobHead.Children.Add(_jobTitle);
        jobBody.Children.Add(jobHead);
        jobBody.Children.Add(_jobProgress);
        jobBody.Children.Add(_jobStatus);
        var hint = MaintUi.Text(L("Vous pouvez continuer à utiliser le PC et changer de page ; gardez Timonier ouvert jusqu'à la fin."), "Pp.Caption");
        hint.Margin = new Thickness(0, 4, 0, 0);
        jobBody.Children.Add(hint);
        _jobBar = new Border { Child = jobBody, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 10) }.Styled("Pp.InfoBar");
        root.Children.Add(_jobBar);

        (_resultBar, _resultText, _resultIcon) = MaintUi.InfoBar();
        _resultBar.Margin = new Thickness(0, 0, 0, 10);
        root.Children.Add(_resultBar);

        var card = new StackPanel();
        foreach (var tool in tools)
        {
            if (card.Children.Count > 0) card.Children.Add(MaintUi.Divider(new Thickness(0, 12, 0, 12)));
            card.Children.Add(BuildRow(tool));
        }
        root.Children.Add(MaintUi.Card(card));
        Root = root;
    }

    private FrameworkElement BuildRow(Tool tool)
    {
        var texts = new StackPanel { Margin = new Thickness(12, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new WrapPanel();
        var title = MaintUi.Text(tool.Title, "Pp.CardTitle", wrap: false);
        title.Margin = new Thickness(0, 0, 8, 0);
        titleRow.Children.Add(title);
        titleRow.Children.Add(MaintUi.Badge(tool.Duration, "Pp.TextSecondary", "Pp.CardSecondary", ""));
        if (tool.Admin)
        {
            var b = MaintUi.Badge(L("Administrateur"), "Pp.TextSecondary", "Pp.CardSecondary", "");
            b.Margin = new Thickness(6, 0, 0, 0);
            titleRow.Children.Add(b);
        }
        texts.Children.Add(titleRow);
        var desc = MaintUi.Text(tool.Description, "Pp.Caption");
        desc.Margin = new Thickness(0, 4, 0, 0);
        texts.Children.Add(desc);

        var button = MaintUi.Button(L("Lancer"), "", "Pp.Button", async (_, _) => await tool.Run());
        button.VerticalAlignment = VerticalAlignment.Center;
        button.MinWidth = 96;
        System.Windows.Automation.AutomationProperties.SetName(button, L("Lancer : {0}", tool.Title));
        _runButtons.Add(button);

        var dock = new DockPanel();
        var icon = MaintUi.IconTile(tool.Glyph);
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(button, Dock.Right);
        dock.Children.Add(icon);
        dock.Children.Add(button);
        dock.Children.Add(texts);
        return dock;
    }

    private void SetBusy(bool busy, string title = "")
    {
        _busy = busy;
        foreach (var b in _runButtons) b.IsEnabled = !busy;
        _jobTitle.Text = title;
        _jobStatus.Text = busy ? L("Préparation…") : "";
        _jobProgress.IsIndeterminate = true;
        _jobProgress.Value = 0;
        _jobBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (busy) _resultBar.Visibility = Visibility.Collapsed;
    }

    private void OnProgress(string text)
    {
        _jobStatus.Text = text;
        var m = PercentRx().Match(text);
        if (m.Success && int.TryParse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value, out var pct) && pct is >= 0 and <= 100)
        {
            _jobProgress.IsIndeterminate = false;
            _jobProgress.Value = pct;
        }
    }

    private void ShowResult(ApplyOutcome outcome)
    {
        if (outcome.Cancelled)
        {
            MaintUi.SetInfo(_resultBar, _resultText, _resultIcon, L("Opération annulée : {0}", outcome.Message), "Pp.InfoBar", "");
            return;
        }
        MaintUi.SetInfo(_resultBar, _resultText, _resultIcon, outcome.Message,
            outcome.Success ? "Pp.InfoBar.Success" : "Pp.InfoBar.Danger", outcome.Success ? "" : "");
    }

    public Task StartSfcAsync() => RunActionJobAsync(RepairSfcAction.ActionId, L("Vérification des fichiers système"),
        L("SFC va analyser tous les fichiers protégés de Windows et remplacer ceux qui sont endommagés. Cela prend 10 à 30 minutes ; une autorisation administrateur sera demandée."));

    private async Task RunActionJobAsync(string actionId, string title, string? confirm)
    {
        if (_busy)
        {
            AppHost.Toasts.Show(L("Une opération de réparation est déjà en cours."), ToastKind.Info);
            return;
        }
        if (confirm is not null && !await AppHost.Dialogs.ConfirmAsync(title, confirm, L("Lancer"), L("Annuler"))) return;

        SetBusy(true, title);
        using var keepAlive = AppHost.Background.Acquire(title);
        try
        {
            var outcome = await AppHost.Engine.RunActionAsync(actionId, null, new Progress<string>(OnProgress));
            ShowResult(outcome);
            AppHost.Toasts.ShowOutcome(outcome);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ResetIconCacheAsync()
    {
        if (_busy) return;
        if (!await AppHost.Dialogs.ConfirmAsync(L("Réinitialiser le cache des icônes"),
                L("L'Explorateur Windows va être fermé quelques secondes : la barre des tâches, le bureau et les fenêtres de dossiers disparaîtront puis reviendront. Les fenêtres de l'Explorateur ouvertes seront fermées."), L("Réinitialiser"), L("Annuler"))) return;

        SetBusy(true, L("Réinitialisation du cache des icônes"));
        try
        {
            var (deleted, locked) = await Task.Run(async () =>
            {
                await ProcessRunner.RunAsync(SystemTool.TaskKill, ["/f", "/im", "explorer.exe"], new RunOptions { Timeout = TimeSpan.FromSeconds(15) });
                try
                {
                    await Task.Delay(1200);
                    return DeleteIconCaches();
                }
                finally
                {
                    try { ProcessRunner.Launch(SystemTool.Explorer); }
                    catch (Exception ex) { Log.Error("Maintenance", "relance de l'Explorateur", ex); }
                }
            });
            var outcome = new ApplyOutcome(true, locked == 0
                ? LP(deleted, "Cache des icônes réinitialisé ({0} fichier supprimé). Les icônes se reconstruisent au fil de l'affichage.",
                    "Cache des icônes réinitialisé ({0} fichiers supprimés). Les icônes se reconstruisent au fil de l'affichage.")
                : L("Cache des icônes réinitialisé ({0}, {1}). Si des icônes restent incorrectes, redémarrez le PC.",
                    LP(deleted, "{0} fichier supprimé", "{0} fichiers supprimés"), LP(locked, "{0} encore utilisé", "{0} encore utilisés")));
            ShowResult(outcome);
            AppHost.Toasts.ShowOutcome(outcome);
        }
        catch (Exception ex)
        {
            Log.Error("Maintenance", "cache des icônes", ex);
            ShowResult(new ApplyOutcome(false, L("La réinitialisation a échoué : {0}", ex.Message)));
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Supprime iconcache_*.db, thumbcache_*.db et l'ancien IconCache.db (suppression sûre, sans suivre de lien).</summary>
    private static (int Deleted, int Locked) DeleteIconCaches()
    {
        int deleted = 0, locked = 0;
        void Delete(string root, IEnumerable<string> files)
        {
            var rootFinal = SafeDelete.RootFinalPath(root);
            if (rootFinal is null) return;
            foreach (var f in files)
            {
                switch (SafeDelete.Delete(f, rootFinal, directory: false))
                {
                    case SafeDelete.Outcome.Deleted: deleted++; break;
                    case SafeDelete.Outcome.Missing: break;
                    default: locked++; break;
                }
            }
        }
        try
        {
            var explorer = CleanupCatalog.ExplorerCacheDir;
            if (Directory.Exists(explorer))
                Delete(explorer, Directory.EnumerateFiles(explorer, "iconcache_*.db").Concat(Directory.EnumerateFiles(explorer, "thumbcache_*.db")).ToList());
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var legacy = Path.Combine(local, "IconCache.db");
            if (File.Exists(legacy)) Delete(local, [legacy]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Maintenance", "cache des icônes : " + ex.Message);
        }
        return (deleted, locked);
    }

    private async Task ResetStoreAsync()
    {
        if (_busy) return;
        SetBusy(true, L("Réinitialisation du cache du Microsoft Store"));
        try
        {
            _jobStatus.Text = L("WSReset vide le cache ; le Microsoft Store s'ouvrira à la fin.");
            var r = await ProcessRunner.RunAsync(SystemTool.WsReset, [], new RunOptions { Timeout = TimeSpan.FromMinutes(3) });
            var outcome = r.TimedOut
                ? new ApplyOutcome(false, L("WSReset n'a pas répondu dans les 3 minutes."))
                : new ApplyOutcome(true, L("Cache du Microsoft Store réinitialisé : le Store devrait s'ouvrir."));
            ShowResult(outcome);
            AppHost.Toasts.ShowOutcome(outcome);
        }
        catch (Exception ex)
        {
            Log.Error("Maintenance", "wsreset", ex);
            ShowResult(new ApplyOutcome(false, L("Impossible de lancer WSReset : {0}", ex.Message)));
        }
        finally
        {
            SetBusy(false);
        }
    }
}
