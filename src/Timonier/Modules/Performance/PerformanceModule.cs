using Timonier.Core.Catalog;
using Timonier.Core.Platform;
using Timonier.Core.Search;

namespace Timonier.Modules.Performance;

/// <summary>
/// Performances : profil matériel, plans et modes d'alimentation, veille, effets visuels, jeux, énergie, arrière-plan,
/// services et stockage. Déclarations uniquement (appelé aussi dans le broker).
/// </summary>
public sealed class PerformanceModule : IModule
{
    public const string Category = "performance";
    public const string PageId = "performance";
    public const string Glyph = "";

    public void Register(ModuleRegistry r)
    {
        r.AddCategory(new CategoryInfo(Category, L("Performance"), Glyph,
            L("Power, sleep, visual effects, gaming, background apps, services and storage.")));

        r.AddTweaks(PerformanceTweaks.All());

        r.AddAction(new ActivateSchemeAction(admin: false));
        r.AddAction(new ActivateSchemeAction(admin: true));
        r.AddAction(new AddSchemeAction());
        r.AddAction(new SetTimeoutAction());
        r.AddAction(new SetPowerModeAction());
        r.AddAction(new DirectXSettingAction());

        r.AddPage(new PageInfo(PageId, L("Performance"), Glyph, NavSection.Settings, 30, () => new PerformancePage())
        {
            CategoryId = Category,
            Description = L("Hardware profile, power plans, power mode, sleep, visual effects, gaming and services."),
            Keywords = [L("performance, speed, slow, speed up, power, energy, battery, sleep, games, gaming, visual effects, services, optimize")],
        });

        // Lecture seule, exécuté hors du thread UI par le tableau de bord.
        r.AddHealthCheck(HealthCheck.Sync("perf.power", L("Power"), "", PageId, PowerHealth.Check));

        r.AddQuickAction(new QuickAction("perf.mode-game", L("Gaming mode"), "",
            L("Turns on Game Mode, stops background recording and applies the GPU scheduling recommendation, after confirmation."),
            PerfQuickActions.GameModeAsync)
        {
            Keywords = [L("game mode, gaming, play, fps, gaming mode, game performance")],
            Order = 40,
        });
        r.AddQuickAction(new QuickAction("perf.mode-eco", L("Power saver"), "",
            L("Switches the PC to “Best power efficiency” mode (or to the Power saver plan) to save battery."),
            PerfQuickActions.EcoModeAsync)
        {
            Keywords = [L("power saving, battery, battery life, power efficiency, power saver, eco, energy saver")],
            Order = 45,
        });

        RegisterSearchEntries(r);

        Synonyms.AddGroup("plan d alimentation", "mode de gestion de l alimentation", "power plan", "power scheme", "powercfg");
        Synonyms.AddGroup("performances optimales", "ultimate performance", "performances maximales");
        Synonyms.AddGroup("veille prolongee", "hibernation", "hiberner", "hibernate");
        Synonyms.AddGroup("mise en veille", "veille", "sleep", "standby");
        Synonyms.AddGroup("lent", "lenteur", "rame", "ralenti", "accelerer", "optimiser", "booster");
        Synonyms.AddGroup("fps", "images par seconde", "framerate", "fluidite jeux");
        Synonyms.AddGroup("planification gpu", "hags", "hardware accelerated gpu scheduling");
        Synonyms.AddGroup("sysmain", "superfetch", "prefetch");
    }

    private static void RegisterSearchEntries(ModuleRegistry r)
    {
        AddSection(r, "perf.section.hardware", L("This PC's hardware profile"), L("Performance level, processor, memory, drive, graphics card"),
            "", "hardware", [L("hardware profile, configuration, specs, processor, cpu, memory, ram, drive, disk, graphics card, gpu, performance level")]);
        AddSection(r, "perf.section.plans", L("Power plans"), L("Activate a plan, add “Ultimate Performance”"),
            "", "power", [L("power plan, ultimate performance, high performance, ultimate, balanced")]);
        AddSection(r, "perf.section.mode", L("Power mode"), L("Power saving, Balanced or Performance, plugged in and on battery"),
            "", "power", [L("power mode, power slider, power efficiency, best performance, best power efficiency")]);
        AddSection(r, "perf.section.sleep", L("Sleep and screen turn-off"), L("Timeouts when plugged in and on battery"),
            "", "sleep", [L("sleep, screen, timeout, turn off screen, display off, screen timeout")]);
        AddSection(r, "perf.section.graphics", L("Game graphics options"), L("Windowed game optimizations, variable refresh rate"),
            "", "graphics", [L("windowed games, vrr, variable refresh rate, directx, flip model, auto hdr")]);

        AddWindows(r, "perf.win.power", L("Power & battery (Windows Settings)"), L("Power mode, sleep, battery saver"),
            "ms-settings:powersleep", [L("power settings, battery power, power and battery")]);
        AddWindows(r, "perf.win.batterysaver", L("Battery saver (Windows Settings)"), L("Turn-on threshold and battery usage"),
            "ms-settings:batterysaver", [L("battery saver, energy saver, power saving, battery usage")]);
        AddWindows(r, "perf.win.gamemode", L("Game Mode (Windows Settings)"), L("Windows gaming settings"),
            "ms-settings:gaming-gamemode", [L("game mode, gaming settings, games")]);
        AddWindows(r, "perf.win.graphics", L("Graphics (Windows Settings)"), L("Graphics card preference per app"),
            "ms-settings:display-advancedgraphics", [L("gpu preference, app graphics card, graphics preference, graphics")]);

        AddTool(r, "perf.tool.fxdialog", L("Performance Options (Windows)"), L("Classic dialog for visual effects and virtual memory"),
            SystemTool.SystemPropertiesPerformance, [], [L("performance options, virtual memory, paging file, pagefile, visual effects")]);
        AddTool(r, "perf.tool.powercpl", L("Power Options (Control Panel)"), L("Advanced power plan settings"),
            SystemTool.Control, ["powercfg.cpl"], [L("power options, advanced power settings, powercfg.cpl")]);
    }

    private static void AddSection(ModuleRegistry r, string id, string title, string subtitle, string glyph, string section, string[] keywords) =>
        r.AddSearchEntry(new SearchEntry
        {
            Id = id, Title = title, Subtitle = subtitle, Glyph = glyph, Keywords = keywords,
            PageId = PageId, PageParameter = "section:" + section,
        });

    private static void AddWindows(ModuleRegistry r, string id, string title, string subtitle, string uri, string[] keywords) =>
        r.AddSearchEntry(new SearchEntry
        {
            Id = id, Title = title, Subtitle = subtitle, Glyph = "", Keywords = keywords,
            Kind = SearchEntryKind.WindowsSetting,
            Execute = () => ProcessRunner.OpenSettingsUri(uri),
        });

    private static void AddTool(ModuleRegistry r, string id, string title, string subtitle, SystemTool tool, string[] args, string[] keywords) =>
        r.AddSearchEntry(new SearchEntry
        {
            Id = id, Title = title, Subtitle = subtitle, Glyph = "", Keywords = keywords,
            Kind = SearchEntryKind.Tool,
            Execute = () => ProcessRunner.Launch(tool, args),
        });
}
