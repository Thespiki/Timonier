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

        r.AddPage(new PageInfo(PageId, L("Setup & profiles"), Glyph, NavSection.Tools, 5, () => new ProfilesPage())
        {
            Description = L("Set up a PC in a few steps using usage profiles: office work, gaming, family, privacy…"),
            Keywords = [L("setup, profile, profiles, wizard, first run, new pc, configure, preset, installation")],
        });

        r.AddQuickAction(new QuickAction("profiles.setup", L("Set up this PC"), Glyph,
            L("Choose usage profiles and apply the matching settings all at once."), () =>
            {
                AppHost.Navigator.Navigate(PageId);
                return Task.CompletedTask;
            })
        {
            Order = 0,
            Keywords = [L("setup, first run, wizard, profile, new pc")],
        });

        r.AddSearchEntries(
        [
            Entry("profiles.firstrun", L("Set up a new PC"), L("Profile-based setup wizard"),
                L("setup, first run, new pc, after installing windows, reinstall, clean install")),
            Entry("profiles.gaming", L("Gaming profile"), L("Game Mode, background recording, GPU scheduling"),
                L("gaming profile, gaming, gamer, optimize for games, play, games")),
            Entry("profiles.office", L("Office work profile"), L("Clean, distraction-free workstation"),
                L("office profile, work, office, desk, remote work, productivity")),
            Entry("profiles.family", L("Family and kids profile"), L("SafeSearch, ads turned off, SmartScreen"),
                L("family profile, kids, children, child, parental, family pc")),
            Entry("profiles.privacy", L("Maximum privacy profile"), L("Telemetry, ads, Copilot and Recall at a minimum"),
                L("privacy profile, maximum privacy, anti telemetry, debloat, privacy")),
            Entry("profiles.lowend", L("Low-end PC profile"), L("Lighten Windows on a low-powered PC"),
                L("slow pc, old pc, small pc, lighten windows, speed up, low end")),
            Entry("profiles.dev", L("Developer profile"), L("File Explorer and tools for programming"),
                L("developer profile, programming, dev, coding")),
            Entry("profiles.export", L("Export the configuration"), L("Save a configuration plan to a file"),
                L("export configuration, back up settings, export, configuration file, clone configuration")),
            Entry("profiles.import", L("Import a configuration"), L("Reuse the settings of another PC (verified before applying)"),
                L("import configuration, import, copy settings, same configuration on multiple pcs")),
            Entry("profiles.rename", L("Rename this PC"), L("Change the computer's name on the network"),
                L("pc name, computer name, rename computer, hostname, device name")),
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
