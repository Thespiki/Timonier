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
        r.AddPage(new PageInfo(JournalPageId, L("Change history"), JournalGlyph, NavSection.App, 10, () => new JournalPage())
        {
            Description = L("Everything Timonier has changed on this PC, with the option to undo it."),
            Keywords = [L("history, change history, log, undo, restore, changes, revert, roll back, go back, csv export")],
        });
        r.AddPage(new PageInfo(TransparencyPageId, L("Transparency"), TransparencyGlyph, NavSection.App, 20, () => new TransparencyPage())
        {
            Description = L("What Timonier can do, how it does it, what it doesn't do, and the data it keeps."),
            Keywords = [L("transparency, timonier security, limits, catalog, operations, registry changes, stored data, timonier privacy, telemetry, background")],
        });
        r.AddPage(new PageInfo(SettingsPageId, L("Timonier settings"), SettingsGlyph, NavSection.App, 30, () => new SettingsPage())
        {
            Description = L("Theme, PIN, admin session, start with Windows, reset."),
            Keywords = [L("settings, preferences, options, timonier settings, theme, pin, advanced mode, start with windows, about, version")],
        });

        r.AddQuickAction(new QuickAction("app.journal", L("Change history"), JournalGlyph,
            L("View and undo changes made by Timonier."),
            () => { AppHost.Navigator.Navigate(JournalPageId); return Task.CompletedTask; })
        {
            Keywords = [L("undo, history, log, revert, roll back, go back, restore a setting")],
            Order = 90,
        });

        // Lecture seule (fichier JSON local + HKLM) : exécuté hors du thread UI par le tableau de bord.
        r.AddHealthCheck(HealthCheck.Sync("app.journal", L("Timonier changes"), JournalGlyph, JournalPageId, () =>
        {
            var all = JournalWriter.UserStore.All().Concat(MachineJournalStore.ReadAll()).ToList();
            var undoable = all.Count(e => e.CanUndo);
            if (all.Count == 0)
                return new HealthResult(HealthStatus.Good, L("No changes"), L("Timonier hasn't changed anything on this PC yet."));
            var today = all.Count(e => e.At.LocalDateTime.Date == DateTime.Today && !e.Undone);
            return new HealthResult(HealthStatus.Info,
                LP(undoable, "{0} change you can undo", "{0} changes you can undo"),
                today == 0 ? L("No changes today.")
                    : LP(today, "{0} change today. Everything can be undone from History.", "{0} changes today. Everything can be undone from History."));
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

        Add("journal.undo", L("Undo a change"), L("History › go back to the previous state"), "", JournalPageId, "filter:undoable",
            L("undo, revert, go back, restore a setting, roll back"));
        Add("journal.undoall", L("Undo all of today's changes"), L("History › bulk undo (session or day)"), "",
            JournalPageId, "filter:undoable", L("undo all, undo everything, undo session, revert all, restore initial state"));
        Add("journal.export", L("Export history (CSV)"), L("History › CSV file you can open in Excel"), "", JournalPageId, null,
            L("export, csv, excel, save history, back up history"));
        Add("journal.clear", L("Clear history"), L("History › clear user history"), "", JournalPageId, null,
            L("clear history, delete history, erase log, empty history"));

        Add("transparency.catalog", L("Everything Timonier can change"), L("Transparency › full list of settings and operations"),
            TransparencyGlyph, TransparencyPageId, "tab:tweaks", L("catalog, operations, registry keys, what timonier changes"));
        Add("transparency.actions", L("Timonier actions"), L("Transparency › parameterized actions, admin, and confirmations"), TransparencyGlyph,
            TransparencyPageId, "tab:actions", L("actions, action list, elevated confirmation"));
        Add("transparency.unavailable", L("Features unavailable on this PC"), L("Transparency › grouped by reason"), "",
            TransparencyPageId, "tab:unavailable", L("unavailable, not available, grayed out, edition, not supported, unsupported"));
        Add("transparency.limits", L("What Timonier can't do"), L("Transparency › honest limits"), "", TransparencyPageId,
            "tab:limits", L("limits, impossible, ctrl alt del, group policy, mdm, default apps"));
        Add("transparency.security", L("Timonier security"), L("Transparency › architecture, elevation, checks"), "",
            TransparencyPageId, "tab:security", L("security, broker, uac, elevation, named pipe, pipe"));
        Add("transparency.data", L("Data stored by Timonier"), L("Transparency › files and registry used"), "",
            TransparencyPageId, "tab:security", L("data, files, storage, localappdata, telemetry, timonier privacy"));
        Add("transparency.background", L("Timonier in the background"), L("Transparency › why the app stays running"), "",
            TransparencyPageId, "tab:security", L("background, notification area, tray, system tray, stays open"));

        Add("settings.theme", L("Timonier theme"), L("Settings › System, light or dark"), "", SettingsPageId, "section:appearance",
            L("theme, dark mode, light mode, light, dark, appearance, timonier appearance, color scheme"));
        Add("settings.pin", L("Timonier PIN"), L("Settings › protect access to the app"), "", SettingsPageId,
            "section:security", L("pin, pin code, password, passcode, lock, protect timonier"));
        Add("settings.startup", L("Start Timonier with Windows"), L("Settings › launch when you sign in"), "", SettingsPageId,
            "section:behavior", L("start with windows, automatic startup, launch at startup, run at startup, autostart, startup"));
        Add("settings.admin", L("Admin session"), L("Settings › auto-close delay, close now"), "",
            SettingsPageId, "section:security", L("admin, administrator, uac, elevation, close admin session, timeout, delay"));
        Add("settings.advanced", L("Advanced mode"), L("Settings › show settings reserved for experienced users"), "",
            SettingsPageId, "section:behavior", L("advanced mode, expert, advanced settings, power user"));
        Add("settings.animations", L("Reduce animations"), L("Settings › simpler, lighter interface"), "", SettingsPageId,
            "section:appearance", L("animations, effects, reduce animations, reduce motion, motion"));
        Add("settings.reset", L("Reset Timonier"), L("Settings › restore default preferences"), "", SettingsPageId,
            "section:data", L("reset, restore defaults, factory reset, default, defaults"));
        Add("settings.about", L("About Timonier"), L("Settings › version, keyboard shortcuts"), "", SettingsPageId, "section:about",
            L("about, version, keyboard shortcuts, hotkeys, help"));
    }
}
