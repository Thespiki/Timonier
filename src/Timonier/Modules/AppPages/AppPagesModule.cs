using Timonier.Core.Catalog;
using Timonier.Core.Engine;
using Timonier.Core.Search;

namespace Timonier.Modules.AppPages;

/// <summary>
/// Pages de l'application elle-même : journal des modifications (annulation), transparence (tout ce que Timonier
/// peut faire, et ne fait pas) et paramètres. Déclarations uniquement : appelé aussi dans le broker.
/// </summary>
public sealed class AppPagesModule : IModule
{
    public const string JournalPageId = "journal";
    public const string TransparencyPageId = "transparency";
    public const string SettingsPageId = "settings";

    internal const string JournalGlyph = "";
    internal const string TransparencyGlyph = "";
    internal const string SettingsGlyph = "";

    public void Register(ModuleRegistry r)
    {
        r.AddPage(new PageInfo(JournalPageId, L("Journal des modifications"), JournalGlyph, NavSection.App, 10, () => new JournalPage())
        {
            Description = L("Tout ce que Timonier a modifié sur ce PC, avec la possibilité d'annuler."),
            Keywords = [L("journal, historique, annuler, undo, restaurer, modifications, changements, revenir en arrière, export csv")],
        });
        r.AddPage(new PageInfo(TransparencyPageId, L("Transparence"), TransparencyGlyph, NavSection.App, 20, () => new TransparencyPage())
        {
            Description = L("Ce que Timonier peut faire, comment il le fait, ce qu'il ne fait pas et les données qu'il conserve."),
            Keywords = [L("transparence, sécurité de timonier, limites, catalogue, opérations, registre modifié, données stockées, confidentialité de timonier, télémétrie, arrière-plan")],
        });
        r.AddPage(new PageInfo(SettingsPageId, L("Paramètres de Timonier"), SettingsGlyph, NavSection.App, 30, () => new SettingsPage())
        {
            Description = L("Thème, code PIN, session administrateur, démarrage avec Windows, réinitialisation."),
            Keywords = [L("paramètres, préférences, options, réglages de timonier, thème, code pin, mode avancé, démarrer avec windows, à propos, version")],
        });

        r.AddQuickAction(new QuickAction("app.journal", L("Journal des modifications"), JournalGlyph,
            L("Voir et annuler les modifications faites par Timonier."),
            () => { AppHost.Navigator.Navigate(JournalPageId); return Task.CompletedTask; })
        {
            Keywords = [L("annuler, historique, journal, undo, revenir en arrière, restaurer un réglage")],
            Order = 90,
        });

        // Lecture seule (fichier JSON local + HKLM) : exécuté hors du thread UI par le tableau de bord.
        r.AddHealthCheck(HealthCheck.Sync("app.journal", L("Modifications de Timonier"), JournalGlyph, JournalPageId, () =>
        {
            var all = JournalWriter.UserStore.All().Concat(MachineJournalStore.ReadAll()).ToList();
            var undoable = all.Count(e => e.CanUndo);
            if (all.Count == 0)
                return new HealthResult(HealthStatus.Good, L("Aucune modification"), L("Timonier n'a encore rien modifié sur ce PC."));
            var today = all.Count(e => e.At.LocalDateTime.Date == DateTime.Today && !e.Undone);
            return new HealthResult(HealthStatus.Info,
                LP(undoable, "{0} modification annulable", "{0} modifications annulables"),
                today == 0 ? L("Aucune modification aujourd'hui.")
                    : LP(today, "{0} modification aujourd'hui. Tout est annulable depuis le journal.", "{0} modifications aujourd'hui. Tout est annulable depuis le journal."));
        }));

        RegisterSearchEntries(r);

        Synonyms.AddGroup("annuler", "undo", "revenir en arriere", "defaire", "restaurer", "retablir");
        Synonyms.AddGroup("journal", "historique", "log", "history");
        Synonyms.AddGroup("code pin", "mot de passe de timonier", "verrouiller timonier", "pin");
        Synonyms.AddGroup("demarrer avec windows", "lancer au demarrage", "demarrage automatique de timonier");
    }

    private static void RegisterSearchEntries(ModuleRegistry r)
    {
        void Add(string id, string title, string subtitle, string glyph, string page, string? param, params string[] keywords) =>
            r.AddSearchEntry(new SearchEntry
            {
                Id = "app." + id, Title = title, Subtitle = subtitle, Glyph = glyph, PageId = page, PageParameter = param, Keywords = keywords,
            });

        Add("journal.undo", L("Annuler une modification"), L("Journal › revenir à l'état précédent"), "", JournalPageId, "filter:undoable",
            L("annuler, undo, revenir en arriere, restaurer un reglage, defaire"));
        Add("journal.undoall", L("Annuler toutes les modifications du jour"), L("Journal › annulation groupée (session ou journée)"), "",
            JournalPageId, "filter:undoable", L("tout annuler, annuler tout, annuler la session, retour etat initial"));
        Add("journal.export", L("Exporter le journal (CSV)"), L("Journal › fichier CSV lisible dans Excel"), "", JournalPageId, null,
            L("export, csv, excel, sauvegarder l'historique"));
        Add("journal.clear", L("Vider le journal"), L("Journal › effacer l'historique utilisateur"), "", JournalPageId, null,
            L("effacer historique, supprimer journal, vider historique"));

        Add("transparency.catalog", L("Tout ce que Timonier peut modifier"), L("Transparence › liste complète des réglages et opérations"),
            TransparencyGlyph, TransparencyPageId, "tab:tweaks", L("catalogue, operations, cles de registre, que modifie timonier"));
        Add("transparency.actions", L("Actions de Timonier"), L("Transparence › actions paramétrées, admin et confirmations"), TransparencyGlyph,
            TransparencyPageId, "tab:actions", L("actions, liste des actions, confirmation elevee"));
        Add("transparency.unavailable", L("Fonctions indisponibles sur ce PC"), L("Transparence › regroupées par raison"), "",
            TransparencyPageId, "tab:unavailable", L("indisponible, pas disponible, grise, edition, non supporte"));
        Add("transparency.limits", L("Ce que Timonier ne peut pas faire"), L("Transparence › limites honnêtes"), "", TransparencyPageId,
            "tab:limits", L("limites, impossible, ctrl alt suppr, strategie de groupe, mdm, applications par defaut"));
        Add("transparency.security", L("Sécurité de Timonier"), L("Transparence › architecture, élévation, vérifications"), "",
            TransparencyPageId, "tab:security", L("securite, broker, uac, elevation, canal nomme, pipe"));
        Add("transparency.data", L("Données stockées par Timonier"), L("Transparence › fichiers et registre utilisés"), "",
            TransparencyPageId, "tab:security", L("donnees, fichiers, stockage, localappdata, telemetrie, vie privee de timonier"));
        Add("transparency.background", L("Timonier en arrière-plan"), L("Transparence › pourquoi l'application reste active"), "",
            TransparencyPageId, "tab:security", L("arriere-plan, zone de notification, tray, reste ouvert"));

        Add("settings.theme", L("Thème de Timonier"), L("Paramètres › Système, clair ou sombre"), "", SettingsPageId, "section:appearance",
            L("theme, mode sombre, dark mode, clair, apparence de timonier"));
        Add("settings.pin", L("Code PIN de Timonier"), L("Paramètres › protéger l'ouverture de l'application"), "", SettingsPageId,
            "section:security", L("code pin, mot de passe, verrouiller, proteger timonier, pin"));
        Add("settings.startup", L("Démarrer Timonier avec Windows"), L("Paramètres › lancement à l'ouverture de session"), "", SettingsPageId,
            "section:behavior", L("demarrer avec windows, demarrage automatique, lancer au demarrage, autostart"));
        Add("settings.admin", L("Session administrateur"), L("Paramètres › délai de fermeture automatique, fermer maintenant"), "",
            SettingsPageId, "section:security", L("admin, administrateur, uac, elevation, fermer la session admin, delai"));
        Add("settings.advanced", L("Mode avancé"), L("Paramètres › afficher les réglages réservés aux utilisateurs avertis"), "",
            SettingsPageId, "section:behavior", L("mode avance, expert, reglages avances"));
        Add("settings.animations", L("Réduire les animations"), L("Paramètres › interface plus sobre et plus légère"), "", SettingsPageId,
            "section:appearance", L("animations, effets, reduire les animations"));
        Add("settings.reset", L("Réinitialiser Timonier"), L("Paramètres › revenir aux préférences par défaut"), "", SettingsPageId,
            "section:data", L("reinitialiser, reset, remise a zero, par defaut"));
        Add("settings.about", L("À propos de Timonier"), L("Paramètres › version, raccourcis clavier"), "", SettingsPageId, "section:about",
            L("a propos, version, raccourcis clavier, aide"));
    }
}
