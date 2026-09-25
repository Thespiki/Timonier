using Timonier.Core.Catalog;

namespace Timonier.Modules.Profiles;

/// <summary>
/// « Installation et profils » : assistant de configuration d'un PC par profils d'usage combinables.
/// Aucun réglage propre : le module compose les réglages des autres modules (par leur identifiant, via le registre).
/// </summary>
public sealed class ProfilesModule : IModule
{
    public const string PageId = "profiles";
    public const string Glyph = "\uE90F";

    public void Register(ModuleRegistry r)
    {
        // Déclarations uniquement : ce code s'exécute aussi dans le broker élevé.
        r.AddAction(new RenameComputerAction());

        r.AddPage(new PageInfo(PageId, "Installation et profils", Glyph, NavSection.Tools, 5, () => new ProfilesPage())
        {
            Description = "Configurer un PC en quelques étapes à partir de profils d'usage : bureautique, jeux, famille, vie privée…",
            Keywords = ["installation", "profil", "profils", "assistant", "premier démarrage", "nouveau pc", "configurer", "setup", "preset"],
        });

        r.AddQuickAction(new QuickAction("profiles.setup", "Configurer ce PC", Glyph,
            "Choisir des profils d'usage et appliquer les réglages adaptés en une fois.", () =>
            {
                AppHost.Navigator.Navigate(PageId);
                return Task.CompletedTask;
            })
        {
            Order = 0,
            Keywords = ["installation", "premier démarrage", "assistant", "profil", "nouveau pc"],
        });

        r.AddSearchEntries(
        [
            Entry("profiles.firstrun", "Configurer un nouveau PC", "Assistant d'installation par profils",
                "installation", "premier démarrage", "nouveau pc", "après installation de windows", "réinstallation", "setup", "first run"),
            Entry("profiles.gaming", "Profil Jeux", "Mode Jeu, enregistrement en arrière-plan, planification GPU",
                "profil jeux", "gaming", "gamer", "optimiser pour les jeux", "jouer"),
            Entry("profiles.office", "Profil Bureautique", "Poste de travail sobre et sans distractions",
                "profil bureautique", "travail", "bureau", "office", "télétravail"),
            Entry("profiles.family", "Profil Famille et enfants", "Recherche sécurisée, publicités coupées, SmartScreen",
                "profil famille", "enfants", "enfant", "parental", "pc familial"),
            Entry("profiles.privacy", "Profil Vie privée maximale", "Télémétrie, publicités, Copilot et Recall au minimum",
                "profil vie privée", "confidentialité maximale", "anti télémétrie", "debloat", "privacy"),
            Entry("profiles.lowend", "Profil PC modeste", "Alléger Windows sur un PC peu puissant",
                "pc lent", "vieux pc", "petit pc", "alléger windows", "accélérer", "low end"),
            Entry("profiles.dev", "Profil Développeur", "Explorateur et outils pour programmer",
                "profil développeur", "programmation", "dev", "coder"),
            Entry("profiles.export", "Exporter la configuration", "Enregistrer un plan de configuration dans un fichier",
                "exporter la configuration", "sauvegarder les réglages", "export", "fichier de configuration", "cloner la configuration"),
            Entry("profiles.import", "Importer une configuration", "Reprendre les réglages d'un autre PC (vérifiés avant application)",
                "importer la configuration", "import", "copier les réglages", "même configuration sur plusieurs pc"),
            Entry("profiles.rename", "Renommer ce PC", "Changer le nom de l'ordinateur sur le réseau",
                "nom du pc", "nom de l'ordinateur", "renommer l'ordinateur", "hostname", "computer name"),
        ]);
    }

    private static SearchEntry Entry(string id, string title, string subtitle, params string[] keywords) => new()
    {
        Id = id,
        Title = title,
        Subtitle = subtitle,
        Glyph = Glyph,
        Keywords = keywords,
        PageId = PageId,
        // « choose » ouvre l'étape des profils ; « choose:<profil> » y coche en plus le profil cherché.
        PageParameter = id switch
        {
            "profiles.gaming" => "choose:" + ProfileCatalog.Gaming,
            "profiles.office" => "choose:" + ProfileCatalog.Office,
            "profiles.family" => "choose:" + ProfileCatalog.Family,
            "profiles.privacy" => "choose:" + ProfileCatalog.PrivacyMax,
            "profiles.lowend" => "choose:" + ProfileCatalog.LowEnd,
            "profiles.dev" => "choose:" + ProfileCatalog.Developer,
            "profiles.export" or "profiles.import" => "choose",
            _ => null,
        },
    };
}
