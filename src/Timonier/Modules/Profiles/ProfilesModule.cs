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

        r.AddPage(new PageInfo(PageId, L("Installation et profils"), Glyph, NavSection.Tools, 5, () => new ProfilesPage())
        {
            Description = L("Configurer un PC en quelques étapes à partir de profils d'usage : bureautique, jeux, famille, vie privée…"),
            Keywords = [L("installation, profil, profils, assistant, premier démarrage, nouveau pc, configurer, setup, preset")],
        });

        r.AddQuickAction(new QuickAction("profiles.setup", L("Configurer ce PC"), Glyph,
            L("Choisir des profils d'usage et appliquer les réglages adaptés en une fois."), () =>
            {
                AppHost.Navigator.Navigate(PageId);
                return Task.CompletedTask;
            })
        {
            Order = 0,
            Keywords = [L("installation, premier démarrage, assistant, profil, nouveau pc")],
        });

        r.AddSearchEntries(
        [
            Entry("profiles.firstrun", L("Configurer un nouveau PC"), L("Assistant d'installation par profils"),
                L("installation, premier démarrage, nouveau pc, après installation de windows, réinstallation, setup, first run")),
            Entry("profiles.gaming", L("Profil Jeux"), L("Mode Jeu, enregistrement en arrière-plan, planification GPU"),
                L("profil jeux, gaming, gamer, optimiser pour les jeux, jouer")),
            Entry("profiles.office", L("Profil Bureautique"), L("Poste de travail sobre et sans distractions"),
                L("profil bureautique, travail, bureau, office, télétravail")),
            Entry("profiles.family", L("Profil Famille et enfants"), L("Recherche sécurisée, publicités coupées, SmartScreen"),
                L("profil famille, enfants, enfant, parental, pc familial")),
            Entry("profiles.privacy", L("Profil Vie privée maximale"), L("Télémétrie, publicités, Copilot et Recall au minimum"),
                L("profil vie privée, confidentialité maximale, anti télémétrie, debloat, privacy")),
            Entry("profiles.lowend", L("Profil PC modeste"), L("Alléger Windows sur un PC peu puissant"),
                L("pc lent, vieux pc, petit pc, alléger windows, accélérer, low end")),
            Entry("profiles.dev", L("Profil Développeur"), L("Explorateur et outils pour programmer"),
                L("profil développeur, programmation, dev, coder")),
            Entry("profiles.export", L("Exporter la configuration"), L("Enregistrer un plan de configuration dans un fichier"),
                L("exporter la configuration, sauvegarder les réglages, export, fichier de configuration, cloner la configuration")),
            Entry("profiles.import", L("Importer une configuration"), L("Reprendre les réglages d'un autre PC (vérifiés avant application)"),
                L("importer la configuration, import, copier les réglages, même configuration sur plusieurs pc")),
            Entry("profiles.rename", L("Renommer ce PC"), L("Changer le nom de l'ordinateur sur le réseau"),
                L("nom du pc, nom de l'ordinateur, renommer l'ordinateur, hostname, computer name")),
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
