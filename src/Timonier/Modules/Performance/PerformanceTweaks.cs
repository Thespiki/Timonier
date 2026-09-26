using Microsoft.Win32;
using Timonier.Core.Model;
using Timonier.Core.Platform;

namespace Timonier.Modules.Performance;

/// <summary>
/// Réglages de performances. Aucun « placebo » : chaque réglage agit sur un paramètre réel de Windows,
/// dont l'effet et les limites sont décrits honnêtement. Les recommandations dépendent du matériel détecté.
/// </summary>
public static class PerformanceTweaks
{
    private const string C = PerformanceModule.Category;

    public static readonly string GroupVisual = L("Visual effects");
    public static readonly string GroupGames = L("Games");
    public static readonly string GroupEnergy = LC("settings group", "Power");
    public static readonly string GroupBackground = L("Background and startup");
    public static readonly string GroupServices = L("Services");
    public static readonly string GroupStorage = L("Storage");

    public static readonly string[] Groups = [GroupVisual, GroupGames, GroupEnergy, GroupBackground, GroupServices, GroupStorage];

    // Clés de registre
    private const string Desktop = @"Control Panel\Desktop";
    private const string WindowMetrics = @"Control Panel\Desktop\WindowMetrics";
    private const string ExplorerAdvanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string VisualEffects = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";
    private const string Dwm = @"Software\Microsoft\Windows\DWM";
    private const string GameBar = @"Software\Microsoft\GameBar";
    private const string GameConfigStore = @"System\GameConfigStore";
    private const string GameDvr = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string GameDvrPolicy = @"SOFTWARE\Policies\Microsoft\Windows\GameDVR";
    private const string GraphicsDrivers = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string SessionPower = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";
    private const string PowerThrottling = @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling";
    private const string BackgroundApps = @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications";
    private const string AppPrivacyPolicy = @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy";
    private const string Serialize = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize";
    private const string FileSystem = @"SYSTEM\CurrentControlSet\Control\FileSystem";

    // Masques UserPreferencesMask écrits par la boîte « Options de performances » de Windows 10/11.
    private static readonly byte[] MaskDefault = [0x9E, 0x1E, 0x07, 0x80, 0x12, 0x00, 0x00, 0x00];
    private static readonly byte[] MaskAppearance = [0x9E, 0x3E, 0x07, 0x80, 0x12, 0x00, 0x00, 0x00];
    private static readonly byte[] MaskPerformance = [0x90, 0x12, 0x03, 0x80, 0x10, 0x00, 0x00, 0x00];

    private static bool LowEnd(SystemProfile p) => p.HardwareLoaded && p.Tier == PerformanceTier.Low;
    private static bool HasDedicatedGpu(SystemProfile p) =>
        p.Gpus.Any(g => (!g.Integrated && g.Vendor is HardwareVendor.Nvidia or HardwareVendor.Amd)
                        || g.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase) && g.Vendor == HardwareVendor.Intel);

    public static IEnumerable<TweakDefinition> All() =>
    [
        .. Visual(), .. Games(), .. Energy(), .. Background(), .. Services(), .. Storage(),
    ];

    // ================================================================== Effets visuels

    private static IEnumerable<TweakDefinition> Visual()
    {
        yield return Tweak.Choice("perf.fx.preset", L("Windows visual effects"),
                L("Equivalent to Windows' “Performance Options” (sysdm.cpl › Advanced › Performance). “Best performance” turns off animations, fades, shadows and previews: the interface feels snappier on a modest PC, without speeding up apps. Unlike the Windows button, font smoothing is kept. Takes effect after you sign out."))
            .In(C, GroupVisual)
            .Keywords(L("visual effects, animations, performance options, appearance, smoothness, shadows, fade"))
            .Tags("lowend")
            .OptionWithHelp("auto", L("Let Windows choose"), L("Original setting: all effects on for most PCs."),
                VisualSet(0, MaskDefault, true))
            .OptionWithHelp("appearance", L("Best appearance"), L("All effects, including the shadow under the mouse pointer."),
                VisualSet(1, MaskAppearance, true))
            .OptionWithHelp("performance", L("Best performance"), L("Turns off animations, fades, shadows and previews (font smoothing kept)."),
                VisualSet(2, MaskPerformance, false))
            .OptionWithHelp("custom", L("Custom"), L("Keeps your individual settings (the ones below)."),
                Reg.CuDword(VisualEffects, "VisualFXSetting", 3))
            .Detect(() => RegistryAccess.ReadDword(RegHive.CurrentUser, VisualEffects, "VisualFXSetting") switch
            {
                null or 0 => "auto",
                1 => "appearance",
                2 => "performance",
                3 => "custom",
                _ => null,
            })
            .WindowsDefault("auto")
            .Effect(ApplyEffect.SignOut)
            .RecommendWhen(p => LowEnd(p) ? "performance" : null)
            .Build();

        yield return Tweak.Toggle("perf.fx.minanimate", L("Window animations (minimize and maximize)"),
                L("Zoom effect when a window is minimized to the taskbar or maximized. Turning it off makes these actions instant; no effect on app speed."))
            .In(C, GroupVisual)
            .Keywords(L("animation, minimize, maximize, window zoom, window animation"))
            .Tags("lowend")
            .WhenOn(Reg.CuString(WindowMetrics, "MinAnimate", "1"))
            .WhenOff(Reg.CuString(WindowMetrics, "MinAnimate", "0"))
            .WindowsDefault(TweakDefinition.On)
            .Effect(ApplyEffect.SignOut)
            .RecommendWhen(p => LowEnd(p) ? TweakDefinition.Off : null)
            .Build();

        yield return Tweak.Toggle("perf.fx.taskbaranim", L("Taskbar animations"),
                L("Animations of taskbar buttons and previews. Turning them off slightly lightens File Explorer on a modest PC."))
            .In(C, GroupVisual)
            .Keywords(L("taskbar, animation, preview, thumbnail"))
            .Tags("lowend")
            .WhenOn(Reg.CuDword(ExplorerAdvanced, "TaskbarAnimations", 1))
            .WhenOff(Reg.CuDword(ExplorerAdvanced, "TaskbarAnimations", 0))
            .WindowsDefault(TweakDefinition.On)
            .Effect(ApplyEffect.SignOut)
            .RecommendWhen(p => LowEnd(p) ? TweakDefinition.Off : null)
            .Build();

        yield return Tweak.Choice("perf.fx.menudelay", L("Submenu opening delay"),
                L("Wait time before a classic submenu opens on hover (context menus, menu bars). A shorter delay feels more responsive, without changing the PC's power at all."))
            .In(C, GroupVisual)
            .Keywords(L("menu, delay, MenuShowDelay, submenu, responsiveness, hover"))
            .Option("400", L("Standard (400 ms)"), Reg.CuString(Desktop, "MenuShowDelay", "400"))
            .Option("200", L("Fast (200 ms)"), Reg.CuString(Desktop, "MenuShowDelay", "200"))
            .Option("50", L("Very fast (50 ms)"), Reg.CuString(Desktop, "MenuShowDelay", "50"))
            .WindowsDefault("400")
            .Effect(ApplyEffect.SignOut)
            .Build();

        yield return Tweak.Toggle("perf.fx.dragfull", L("Window contents while dragging"),
                L("Shows the whole window while you move or resize it. When off, only an outline follows the mouse: mainly useful on a very weak graphics card or over Remote Desktop."))
            .In(C, GroupVisual)
            .Keywords(L("move, drag, resize, window contents, show window contents while dragging"))
            .Tags("lowend")
            .WhenOn(Reg.CuString(Desktop, "DragFullWindows", "1"))
            .WhenOff(Reg.CuString(Desktop, "DragFullWindows", "0"))
            .WindowsDefault(TweakDefinition.On)
            .Effect(ApplyEffect.SignOut)
            .Build();

        yield return Tweak.Toggle("perf.fx.peek", L("Desktop preview (Peek)"),
                L("Makes windows transparent to show the desktop when you hover over the right corner of the taskbar. Turning it off has only a minimal effect on performance."))
            .In(C, GroupVisual)
            .Keywords(L("peek, aero peek, desktop preview, taskbar corner, show desktop"))
            .WhenOn(Reg.CuDword(Dwm, "EnableAeroPeek", 1))
            .WhenOff(Reg.CuDword(Dwm, "EnableAeroPeek", 0))
            .WindowsDefault(TweakDefinition.On)
            .Effect(ApplyEffect.SignOut)
            .Build();

        yield return Tweak.Toggle("perf.fx.iconshadow", L("Shadow under desktop icon labels"),
                L("Drop shadow behind icon text to keep it readable on any wallpaper. Negligible impact on performance."))
            .In(C, GroupVisual)
            .Keywords(L("shadow, icons, desktop, drop shadow, icon text, icon labels"))
            .WhenOn(Reg.CuDword(ExplorerAdvanced, "ListviewShadow", 1))
            .WhenOff(Reg.CuDword(ExplorerAdvanced, "ListviewShadow", 0))
            .WindowsDefault(TweakDefinition.On)
            .Effect(ApplyEffect.RestartExplorer)
            .Build();

        yield return Tweak.Toggle("perf.fx.alphaselect", L("Translucent selection rectangle"),
                L("Semi-transparent blue rectangle when you select files with the mouse. When off, a simple dotted outline is shown."))
            .In(C, GroupVisual)
            .Keywords(L("selection, rectangle, translucent, file explorer, explorer"))
            .WhenOn(Reg.CuDword(ExplorerAdvanced, "ListviewAlphaSelect", 1))
            .WhenOff(Reg.CuDword(ExplorerAdvanced, "ListviewAlphaSelect", 0))
            .WindowsDefault(TweakDefinition.On)
            .Effect(ApplyEffect.RestartExplorer)
            .Build();
    }

    /// <summary>Préréglage d'effets : case de la boîte Windows + masque des effets + réglages individuels cohérents.</summary>
    private static Operation[] VisualSet(int fxSetting, byte[] mask, bool effects) =>
    [
        Reg.CuDword(VisualEffects, "VisualFXSetting", fxSetting),
        Reg.Cu(Desktop, "UserPreferencesMask", RegistryValueKind.Binary, mask),
        Reg.CuString(WindowMetrics, "MinAnimate", effects ? "1" : "0"),
        Reg.CuDword(ExplorerAdvanced, "TaskbarAnimations", effects ? 1 : 0),
        Reg.CuDword(Dwm, "EnableAeroPeek", effects ? 1 : 0),
        Reg.CuDword(ExplorerAdvanced, "ListviewAlphaSelect", effects ? 1 : 0),
        Reg.CuDword(ExplorerAdvanced, "ListviewShadow", effects ? 1 : 0),
        Reg.CuString(Desktop, "DragFullWindows", effects ? "1" : "0"),
    ];

    // ================================================================== Jeux

    private static IEnumerable<TweakDefinition> Games()
    {
        yield return Tweak.Toggle("perf.game.mode", L("Game Mode"),
                L("When a game is detected, Windows gives it priority (processor, graphics card) and holds off driver installations and Windows Update restart notifications. No effect outside games; on by default."))
            .In(C, GroupGames)
            .Keywords(L("game mode, gaming, games, fps"))
            .Tags("gaming")
            .WhenOn(Reg.CuDword(GameBar, "AutoGameModeEnabled", 1))
            .WhenOff(Reg.CuDword(GameBar, "AutoGameModeEnabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("perf.game.capture", L("Game captures (Xbox Game Bar)"),
                L("Screenshots and video recordings of games with the Xbox Game Bar (Win + Alt + Print Screen). When off, the Game Bar can no longer record and Windows no longer prepares capture when games launch."))
            .In(C, GroupGames)
            .Keywords(L("game dvr, capture, recording, xbox game bar, game video, clip, screen recording"))
            .Tags("gaming", "lowend")
            .WhenOn(Reg.CuDword(GameConfigStore, "GameDVR_Enabled", 1), Reg.CuDword(GameDvr, "AppCaptureEnabled", 1),
                Reg.LmDel(GameDvrPolicy, "AllowGameDVR"))
            .WhenOff(Reg.CuDword(GameConfigStore, "GameDVR_Enabled", 0), Reg.CuDword(GameDvr, "AppCaptureEnabled", 0),
                Reg.LmDword(GameDvrPolicy, "AllowGameDVR", 0))
            .WindowsDefault(TweakDefinition.On)
            .RecommendWhen(p => LowEnd(p) ? TweakDefinition.Off : null)
            .Build();

        yield return Tweak.Toggle("perf.game.bgrecord", L("Background recording"),
                L("Continuously records the last few minutes of gameplay so you can “record what happened”. Constant video encoding costs frames per second and battery life: off by default in Windows."))
            .In(C, GroupGames)
            .Keywords(L("record what happened, background recording, replay, dvr, continuous recording"))
            .Tags("gaming", "lowend", "battery")
            .WhenOn(Reg.CuDword(GameDvr, "HistoricalCaptureEnabled", 1))
            .WhenOff(Reg.CuDword(GameDvr, "HistoricalCaptureEnabled", 0))
            .WindowsDefault(TweakDefinition.Off)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("perf.game.nexus", L("Open Game Bar with the Xbox button"),
                L("The Xbox button on a controller opens the Xbox Game Bar. Turn this off if you open it by mistake while playing or if another launcher (Steam) uses this button."))
            .In(C, GroupGames)
            .Keywords(L("controller, gamepad, xbox button, game bar, nexus"))
            .Tags("gaming")
            .WhenOn(Reg.CuDword(GameBar, "UseNexusForGameBarEnabled", 1))
            .WhenOff(Reg.CuDword(GameBar, "UseNexusForGameBarEnabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Choice("perf.game.hags", L("Hardware-accelerated GPU scheduling"),
                L("The graphics card manages its own work queue instead of Windows, which can slightly reduce latency and processor load in games. Requires a recent card with a WDDM 2.7 or later driver (NVIDIA GTX 10xx and later, AMD RX 5000 and later, Intel Arc); gains vary by game."))
            .In(C, GroupGames)
            .Keywords(L("hags, gpu scheduling, hardware accelerated gpu scheduling, graphics card, latency, wddm, dlss 3, frame generation"))
            .Tags("gaming")
            .Option("default", L("Driver's choice"), Reg.LmDel(GraphicsDrivers, "HwSchMode"))
            .Option("on", LC("feminine", "On"), Reg.LmDword(GraphicsDrivers, "HwSchMode", 2))
            .Option("off", L("Disabled"), Reg.LmDword(GraphicsDrivers, "HwSchMode", 1))
            .WindowsDefault("default")
            .Requires(new Requirement { MinBuild = 19041 }.And(Requires.When(HasDedicatedGpu,
                L("Requires a recent dedicated graphics card (NVIDIA, AMD or Intel Arc) with a WDDM 2.7 or later driver."))))
            .Effect(ApplyEffect.Reboot)
            .RecommendWhen(p => HasDedicatedGpu(p) && p.Tier != PerformanceTier.Low ? "on" : null)
            .Build();
    }

    // ================================================================== Énergie

    private static IEnumerable<TweakDefinition> Energy()
    {
        yield return Tweak.Toggle("perf.power.hibernate", L("Hibernation"),
                L("Saves your session to disk, then shuts the PC down completely: no power draw, and you pick up where you left off. The hiberfil.sys file takes up about 40% of RAM. Turning it off frees this space but also removes Fast startup and automatic hibernation (a sleeping laptop could then drain its battery completely)."))
            .In(C, GroupEnergy)
            .Keywords(L("hibernation, hibernate, hiberfil, disk space"))
            .Tags("battery")
            .WhenOn(Sys.Tool(SystemTool.PowerCfg, true, L("Turns on hibernation (powercfg /hibernate on)"), "/hibernate", "on"))
            .WhenOff(Sys.Tool(SystemTool.PowerCfg, true, L("Turns off hibernation and deletes hiberfil.sys (powercfg /hibernate off)"), "/hibernate", "off"))
            .Detect(() => PowerApi.HibernationEnabled() ? TweakDefinition.On : TweakDefinition.Off)
            .WindowsDefault(TweakDefinition.On)
            .Risk(RiskLevel.Moderate)
            .Warning(L("Turning off hibernation also turns off Fast startup."))
            .RecommendWhen(p => p.HasBattery ? TweakDefinition.On : null)
            .Build();

        yield return Tweak.Toggle("perf.power.faststartup", L("Fast startup"),
                L("At shutdown, Windows signs you out but puts the kernel into hibernation: the next startup is faster, especially on a hard drive. Downsides: the PC isn't truly “fresh” after a shutdown (only “Restart” does that), some updates and drivers wait for a restart, and Windows drives stay locked in dual-boot setups (Linux)."))
            .In(C, GroupEnergy)
            .Keywords(L("fast startup, hiberboot, shutdown, boot, dual boot, fast boot"))
            .WhenOn(Reg.LmDword(SessionPower, "HiberbootEnabled", 1))
            .WhenOff(Reg.LmDword(SessionPower, "HiberbootEnabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Requires(Requires.When(_ => PowerApi.HibernationEnabled(),
                L("Requires hibernation: turn it on first (setting above).")))
            .RecommendWhen(p => p.SystemDiskIsHdd ? TweakDefinition.On : null)
            .Build();

        yield return Tweak.Toggle("perf.power.throttling", L("Power throttling for background tasks"),
                L("Power Throttling: Windows runs background apps on the most power-efficient cores and frequencies. This is what preserves a laptop's battery life. Turning it off is only useful for a desktop PC on AC power whose background processing (encoding, computing) is abnormally slow."))
            .In(C, GroupEnergy)
            .Keywords(L("power throttling, ecoqos, throttling, background, efficiency, battery life"))
            .Tags("battery")
            .WhenOn(Reg.LmDel(PowerThrottling, "PowerThrottlingOff"))
            .WhenOff(Reg.LmDword(PowerThrottling, "PowerThrottlingOff", 1))
            .WindowsDefault(TweakDefinition.On)
            .Risk(RiskLevel.Moderate)
            .Effect(ApplyEffect.Reboot)
            .Warning(L("On a laptop, turning it off reduces battery life."))
            .RecommendWhen(p => p.HasBattery ? TweakDefinition.On : null)
            .Build();
    }

    // ================================================================== Arrière-plan et démarrage

    private static IEnumerable<TweakDefinition> Background()
    {
        yield return Tweak.Toggle("perf.bg.apps", L("Background apps"),
                L("Allows Microsoft Store apps to run in the background (receiving notifications, updating tiles, syncing). Blocking them saves a little memory and battery."))
            .In(C, GroupBackground)
            .Keywords(L("background, background apps, apps running in background, battery, notifications"))
            .Tags("battery", "lowend")
            .WhenOn(Reg.CuDword(BackgroundApps, "GlobalUserDisabled", 0))
            .WhenOff(Reg.CuDword(BackgroundApps, "GlobalUserDisabled", 1))
            .WindowsDefault(TweakDefinition.On)
            .Requires(Requires.Windows10Only)
            .Risk(RiskLevel.Moderate)
            .Warning(L("Some apps (Mail, Calendar, Phone…) will no longer receive notifications while they're closed."))
            .RecommendWhen(p => LowEnd(p) ? TweakDefinition.Off : null)
            .Build();

        yield return Tweak.Toggle("perf.bg.apps.policy", L("Store apps in the background"),
                L("“Let Windows apps run in the background” policy (Windows 11 no longer has a global switch). When blocked, no Microsoft Store app runs until it's opened: a little memory and battery saved. Classic (Win32) apps aren't affected."))
            .In(C, GroupBackground)
            .Keywords(L("background, background apps, LetAppsRunInBackground, store apps, battery"))
            .Tags("battery", "lowend")
            .Labels(LC("feminine plural", "Allowed"), LC("feminine plural", "Blocked"))
            .WhenOn(Reg.LmDel(AppPrivacyPolicy, "LetAppsRunInBackground"))
            .WhenOff(Reg.LmDword(AppPrivacyPolicy, "LetAppsRunInBackground", 2))
            .WindowsDefault(TweakDefinition.On)
            .Requires(Requires.Windows11)
            .Risk(RiskLevel.Moderate)
            .Warning(L("Store apps (Teams, WhatsApp, Phone, Mail…) will no longer receive notifications while they're closed."))
            .Build();

        yield return Tweak.Toggle("perf.boot.startupdelay", L("Startup apps delay"),
                L("After you sign in, File Explorer waits about ten seconds before launching startup programs, so the desktop becomes usable sooner. Without this delay, they start immediately: worthwhile on a fast SSD, counterproductive on a hard drive or a modest PC."))
            .In(C, GroupBackground)
            .Keywords(L("startup, startup delay, sign-in, startup programs, StartupDelayInMSec"))
            .Tags("office")
            .Labels(L("Delay on"), L("Launch immediately"))
            .WhenOn(Reg.CuDel(Serialize, "StartupDelayInMSec"))
            .WhenOff(Reg.CuDword(Serialize, "StartupDelayInMSec", 0))
            .WindowsDefault(TweakDefinition.On)
            .RecommendWhen(p => !p.HardwareLoaded ? null
                : p.SystemDiskIsHdd || p.Tier == PerformanceTier.Low ? TweakDefinition.On
                : p.Tier == PerformanceTier.High && p.SystemDisk?.Media is DiskMedia.Ssd or DiskMedia.Nvme ? TweakDefinition.Off
                : null)
            .Build();
    }

    // ================================================================== Services

    private static IEnumerable<TweakDefinition> Services()
    {
        yield return Tweak.Toggle("perf.svc.sysmain", L("SysMain (app preloading)"),
                L("Formerly SuperFetch: keeps the apps you use often in memory so they launch faster. Essential on a hard drive. On an SSD the gain is smaller; only turn it off if you notice abnormal and sustained disk activity."))
            .In(C, GroupServices)
            .Keywords(L("sysmain, superfetch, prefetch, preloading, 100% disk, disk usage, service"))
            .Labels(L("Active"), L("Off"))
            .WhenOn(Sys.Service("SysMain", ServiceStartKind.Automatic))
            .WhenOff(Sys.Service("SysMain", ServiceStartKind.Disabled))
            .WindowsDefault(TweakDefinition.On)
            .Risk(RiskLevel.Moderate)
            .RecommendWhen(p => p.SystemDiskIsHdd ? TweakDefinition.On : null)
            .Build();

        yield return Tweak.Toggle("perf.svc.wsearch", L("Windows Search indexing"),
                L("Windows Search service: indexes the names and contents of files, Outlook emails and settings for instant results. When off, search in the Start menu and File Explorer still works but becomes slow and no longer searches inside document contents."))
            .In(C, GroupServices)
            .Keywords(L("indexing, windows search, wsearch, search, indexer, service"))
            .Tags("lowend")
            .Labels(L("Active"), L("Off"))
            .WhenOn(Sys.Service("WSearch", ServiceStartKind.AutomaticDelayed))
            .WhenOff(Sys.Service("WSearch", ServiceStartKind.Disabled))
            .WindowsDefault(TweakDefinition.On)
            .Risk(RiskLevel.Moderate)
            .Warning(L("Search in Outlook, the Start menu and File Explorer will be much slower."))
            .RecommendWhen(p => p.SystemDiskIsHdd && p.Tier == PerformanceTier.Low ? TweakDefinition.Off : null)
            .Build();

        yield return Tweak.Toggle("perf.svc.xbox", L("Xbox services"),
                L("Xbox Live authentication, cloud game saves, Xbox networking and Xbox accessories (XblAuthManager, XblGameSave, XboxNetApiSvc, XboxGipSvc). They only start on demand: disabling them gains almost nothing if you don't play, but breaks Game Pass and Microsoft Store games if you do."))
            .In(C, GroupServices)
            .Keywords(L("xbox, xbox live, game pass, xbox services, game saves, xbox controller"))
            .Tags("office")
            .Labels(L("On demand"), LC("plural", "Disabled"))
            .WhenOn(Sys.Service("XblAuthManager", ServiceStartKind.Manual), Sys.Service("XblGameSave", ServiceStartKind.Manual),
                Sys.Service("XboxNetApiSvc", ServiceStartKind.Manual), Sys.Service("XboxGipSvc", ServiceStartKind.Manual))
            .WhenOff(Sys.Service("XblAuthManager", ServiceStartKind.Disabled), Sys.Service("XblGameSave", ServiceStartKind.Disabled),
                Sys.Service("XboxNetApiSvc", ServiceStartKind.Disabled), Sys.Service("XboxGipSvc", ServiceStartKind.Disabled))
            .WindowsDefault(TweakDefinition.On)
            .Risk(RiskLevel.Moderate)
            .Warning(L("Game Pass, Microsoft Store games, Xbox saves and Xbox controller updates will no longer work."))
            .Build();

        yield return Tweak.Toggle("perf.svc.maps", L("Downloaded Maps Manager (automatic start)"),
                L("MapsBroker service: updates offline maps for the Maps app. With manual startup, it only runs if an app needs it; a small gain with no downside."))
            .In(C, GroupServices)
            .Keywords(L("maps, mapsbroker, offline maps, service"))
            .Tags("lowend")
            .Labels(L("Automatic"), L("Manual"))
            .WhenOn(Sys.Service("MapsBroker", ServiceStartKind.AutomaticDelayed))
            .WhenOff(Sys.Service("MapsBroker", ServiceStartKind.Manual))
            .WindowsDefault(TweakDefinition.On)
            .RecommendWhen(p => LowEnd(p) ? TweakDefinition.Off : null)
            .Build();

        yield return Tweak.Toggle("perf.svc.fax", L("Fax service"),
                L("Only needed to send or receive faxes with a fax modem. It only starts on demand: disabling it frees almost nothing. Not present on recent installations."))
            .In(C, GroupServices)
            .Keywords(L("fax, service"))
            .Labels(L("On demand"), L("Off"))
            .WhenOn(Sys.Service("Fax", ServiceStartKind.Manual))
            .WhenOff(Sys.Service("Fax", ServiceStartKind.Disabled))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("perf.svc.retaildemo", L("Retail Demo Service"),
                L("Only used for the “demo” mode of PCs on display in stores. Useless on a personal PC; disabling it is safe."))
            .In(C, GroupServices)
            .Keywords(L("retaildemo, store demo, retail demo, service"))
            .Labels(L("On demand"), L("Off"))
            .WhenOn(Sys.Service("RetailDemo", ServiceStartKind.Manual))
            .WhenOff(Sys.Service("RetailDemo", ServiceStartKind.Disabled))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("perf.svc.wmpnetwork", L("Windows Media Player network sharing"),
                L("Shares the legacy Windows Media Player library with other devices (DLNA). It only starts on demand: disabling it gains nothing as long as sharing isn't used, but reduces the network attack surface."))
            .In(C, GroupServices)
            .Keywords(L("wmpnetworksvc, windows media, dlna, media sharing, service"))
            .Labels(L("On demand"), L("Off"))
            .WhenOn(Sys.Service("WMPNetworkSvc", ServiceStartKind.Manual))
            .WhenOff(Sys.Service("WMPNetworkSvc", ServiceStartKind.Disabled))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("perf.svc.wisvc", L("Windows Insider Program service"),
                L("Only needed to receive preview builds of Windows (Insider Program). Starts on demand."))
            .In(C, GroupServices)
            .Keywords(L("insider, wisvc, preview, preview builds, service"))
            .Labels(L("On demand"), L("Off"))
            .WhenOn(Sys.Service("wisvc", ServiceStartKind.Manual))
            .WhenOff(Sys.Service("wisvc", ServiceStartKind.Disabled))
            .WindowsDefault(TweakDefinition.On)
            .Warning(L("The Windows Insider Program will no longer work on this PC."))
            .Build();
    }

    // ================================================================== Stockage

    private static IEnumerable<TweakDefinition> Storage()
    {
        yield return Tweak.Choice("perf.storage.lastaccess", L("NTFS last access timestamp"),
                L("NTFS can record the last access date of every file that's read, which adds writes. Since Windows 10 1803, Windows manages this setting on its own (on only for small system volumes): only change it if backup or archiving software needs this date. Takes effect after a restart."))
            .In(C, GroupStorage)
            .Keywords(L("ntfs, last access, timestamp, fsutil, disk"))
            .OptionWithHelp("system", L("Managed by Windows"), L("Original value (0x80000002)."),
                Reg.LmDword(FileSystem, "NtfsDisableLastAccessUpdate", unchecked((int)0x80000002)))
            .OptionWithHelp("off", L("Always off"), L("No last access date is recorded (0x80000001)."),
                Reg.LmDword(FileSystem, "NtfsDisableLastAccessUpdate", unchecked((int)0x80000001)))
            .OptionWithHelp("on", L("Always on"), L("Required by some archiving tools (0x80000000)."),
                Reg.LmDword(FileSystem, "NtfsDisableLastAccessUpdate", unchecked((int)0x80000000)))
            .Detect(() => RegistryAccess.ReadDword(RegHive.LocalMachine, FileSystem, "NtfsDisableLastAccessUpdate") switch
            {
                null => "system",
                int v when (v & 2) != 0 => "system",
                int v when (v & 1) != 0 => "off",
                _ => "on",
            })
            .WindowsDefault("system")
            .Risk(RiskLevel.Advanced)
            .Effect(ApplyEffect.Reboot)
            .Build();

        yield return Tweak.Toggle("perf.storage.trim", L("SSD TRIM"),
                L("Windows tells the SSD which blocks have been freed (fsutil behavior DisableDeleteNotify). Essential to preserve an SSD's performance and lifespan; on by default. Only turn it off if a manufacturer asks you to."))
            .In(C, GroupStorage)
            .Keywords(L("trim, ssd, nvme, DisableDeleteNotify, ssd optimization, fsutil"))
            .WhenOn(Reg.LmDword(FileSystem, "DisableDeleteNotification", 0))
            .WhenOff(Reg.LmDword(FileSystem, "DisableDeleteNotification", 1))
            .WindowsDefault(TweakDefinition.On)
            .Requires(Requires.When(p => !p.HardwareLoaded || p.Disks.Count == 0 || p.Disks.Any(d => d.Media is DiskMedia.Ssd or DiskMedia.Nvme),
                L("No SSD detected on this PC.")))
            .Risk(RiskLevel.Moderate)
            .Warning(L("Without TRIM, an SSD gradually slows down and wears out faster."))
            .Recommend(TweakDefinition.On)
            .Effect(ApplyEffect.Reboot)
            .Build();

        yield return Tweak.Toggle("perf.storage.thumbnails", L("File thumbnails in File Explorer"),
                L("Previews of images, videos and documents instead of icons. When off, File Explorer opens folders containing lots of photos faster, especially on a hard drive or a slow USB drive."))
            .In(C, GroupStorage)
            .Keywords(L("thumbnails, preview, icons, file explorer, explorer, photos"))
            .Tags("lowend")
            .WhenOn(Reg.CuDword(ExplorerAdvanced, "IconsOnly", 0))
            .WhenOff(Reg.CuDword(ExplorerAdvanced, "IconsOnly", 1))
            .WindowsDefault(TweakDefinition.On)
            .Build();
    }
}
