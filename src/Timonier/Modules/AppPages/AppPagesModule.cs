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
        r.AddPage(new PageInfo(JournalPageId, "Journal des modifications", JournalGlyph, NavSection.App, 10, () => new JournalPage())
        {
            Description = "Tout ce que Timonier a modifié sur ce PC, avec la possibilité d'annuler.",
            Keywords = ["journal", "historique", "annuler", "undo", "restaurer", "modifications", "changements", "revenir en arrière", "export csv"],
        });
        r.AddPage(new PageInfo(TransparencyPageId, "Transparence", TransparencyGlyph, NavSection.App, 20, () => new TransparencyPage())
        {
            Description = "Ce que Timonier peut faire, comment il le fait, ce qu'il ne fait pas et les données qu'il conserve.",
            Keywords = ["transparence", "sécurité de pc pilot", "limites", "catalogue", "opérations", "registre modifié",
                        "données stockées", "confidentialité de pc pilot", "télémétrie", "arrière-plan"],
        });
        r.AddPage(new PageInfo(SettingsPageId, "Paramètres de Timonier", SettingsGlyph, NavSection.App, 30, () => new SettingsPage())
        {
            Description = "Thème, code PIN, session administrateur, démarrage avec Windows, réinitialisation.",
            Keywords = ["paramètres", "préférences", "options", "réglages de pc pilot", "thème", "code pin", "mode avancé",
                        "démarrer avec windows", "à propos", "version"],
        });

        r.AddQuickAction(new QuickAction("app.journal", "Journal des modifications", JournalGlyph,
            "Voir et annuler les modifications faites par Timonier.",
            () => { AppHost.Navigator.Navigate(JournalPageId); return Task.CompletedTask; })
        {
            Keywords = ["annuler", "historique", "journal", "undo", "revenir en arrière", "restaurer un réglage"],
            Order = 90,
        });

        // Lecture seule (fichier JSON local + HKLM) : exécuté hors du thread UI par le tableau de bord.
        r.AddHealthCheck(HealthCheck.Sync("app.journal", "Modifications de Timonier", JournalGlyph, JournalPageId, () =>
        {
            var all = JournalWriter.UserStore.All().Concat(MachineJournalStore.ReadAll()).ToList();
            var undoable = all.Count(e => e.CanUndo);
            if (all.Count == 0)
                return new HealthResult(HealthStatus.Good, "Aucune modification", "Timonier n'a encore rien modifié sur ce PC.");
            var today = all.Count(e => e.At.LocalDateTime.Date == DateTime.Today && !e.Undone);
            return new HealthResult(HealthStatus.Info,
                undoable <= 1 ? $"{undoable} modification annulable" : $"{undoable} modifications annulables",
                today == 0 ? "Aucune modification aujourd'hui." : $"{today} modification(s) aujourd'hui. Tout est annulable depuis le journal.");
        }));

        RegisterSearchEntries(r);

        Synonyms.AddGroup("annuler", "undo", "revenir en arriere", "defaire", "restaurer", "retablir");
        Synonyms.AddGroup("journal", "historique", "log", "history");
        Synonyms.AddGroup("code pin", "mot de passe de pc pilot", "verrouiller pc pilot", "pin");
        Synonyms.AddGroup("demarrer avec windows", "lancer au demarrage", "demarrage automatique de pc pilot");
    }

    private static void RegisterSearchEntries(ModuleRegistry r)
    {
        void Add(string id, string title, string subtitle, string glyph, string page, string? param, params string[] keywords) =>
            r.AddSearchEntry(new SearchEntry
            {
                Id = "app." + id, Title = title, Subtitle = subtitle, Glyph = glyph, PageId = page, PageParameter = param, Keywords = keywords,
            });

        Add("journal.undo", "Annuler une modification", "Journal › revenir à l'état précédent", "", JournalPageId, "filter:undoable",
            "annuler", "undo", "revenir en arriere", "restaurer un reglage", "defaire");
        Add("journal.undoall", "Annuler toutes les modifications du jour", "Journal › annulation groupée (session ou journée)", "",
            JournalPageId, "filter:undoable", "tout annuler", "annuler tout", "annuler la session", "retour etat initial");
        Add("journal.export", "Exporter le journal (CSV)", "Journal › fichier CSV lisible dans Excel", "", JournalPageId, null,
            "export", "csv", "excel", "sauvegarder l'historique");
        Add("journal.clear", "Vider le journal", "Journal › effacer l'historique utilisateur", "", JournalPageId, null,
            "effacer historique", "supprimer journal", "vider historique");

        Add("transparency.catalog", "Tout ce que Timonier peut modifier", "Transparence › liste complète des réglages et opérations",
            TransparencyGlyph, TransparencyPageId, "tab:tweaks", "catalogue", "operations", "cles de registre", "que modifie pc pilot");
        Add("transparency.actions", "Actions de Timonier", "Transparence › actions paramétrées, admin et confirmations", TransparencyGlyph,
            TransparencyPageId, "tab:actions", "actions", "liste des actions", "confirmation elevee");
        Add("transparency.unavailable", "Fonctions indisponibles sur ce PC", "Transparence › regroupées par raison", "",
            TransparencyPageId, "tab:unavailable", "indisponible", "pas disponible", "grise", "edition", "non supporte");
        Add("transparency.limits", "Ce que Timonier ne peut pas faire", "Transparence › limites honnêtes", "", TransparencyPageId,
            "tab:limits", "limites", "impossible", "ctrl alt suppr", "strategie de groupe", "mdm", "applications par defaut");
        Add("transparency.security", "Sécurité de Timonier", "Transparence › architecture, élévation, vérifications", "",
            TransparencyPageId, "tab:security", "securite", "broker", "uac", "elevation", "canal nomme", "pipe");
        Add("transparency.data", "Données stockées par Timonier", "Transparence › fichiers et registre utilisés", "",
            TransparencyPageId, "tab:security", "donnees", "fichiers", "stockage", "localappdata", "telemetrie", "vie privee de pc pilot");
        Add("transparency.background", "Timonier en arrière-plan", "Transparence › pourquoi l'application reste active", "",
            TransparencyPageId, "tab:security", "arriere-plan", "zone de notification", "tray", "reste ouvert");

        Add("settings.theme", "Thème de Timonier", "Paramètres › Système, clair ou sombre", "", SettingsPageId, "section:appearance",
            "theme", "mode sombre", "dark mode", "clair", "apparence de pc pilot");
        Add("settings.pin", "Code PIN de Timonier", "Paramètres › protéger l'ouverture de l'application", "", SettingsPageId,
            "section:security", "code pin", "mot de passe", "verrouiller", "proteger pc pilot", "pin");
        Add("settings.startup", "Démarrer Timonier avec Windows", "Paramètres › lancement à l'ouverture de session", "", SettingsPageId,
            "section:behavior", "demarrer avec windows", "demarrage automatique", "lancer au demarrage", "autostart");
        Add("settings.admin", "Session administrateur", "Paramètres › délai de fermeture automatique, fermer maintenant", "",
            SettingsPageId, "section:security", "admin", "administrateur", "uac", "elevation", "fermer la session admin", "delai");
        Add("settings.advanced", "Mode avancé", "Paramètres › afficher les réglages réservés aux utilisateurs avertis", "",
            SettingsPageId, "section:behavior", "mode avance", "expert", "reglages avances");
        Add("settings.animations", "Réduire les animations", "Paramètres › interface plus sobre et plus légère", "", SettingsPageId,
            "section:appearance", "animations", "effets", "reduire les animations");
        Add("settings.reset", "Réinitialiser Timonier", "Paramètres › revenir aux préférences par défaut", "", SettingsPageId,
            "section:data", "reinitialiser", "reset", "remise a zero", "par defaut");
        Add("settings.about", "À propos de Timonier", "Paramètres › version, raccourcis clavier", "", SettingsPageId, "section:about",
            "a propos", "version", "raccourcis clavier", "aide");
    }
}
