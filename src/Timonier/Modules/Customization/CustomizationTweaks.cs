using System.Globalization;
using Microsoft.Win32;
using Timonier.Core.Model;
using Timonier.Core.Platform;

namespace Timonier.Modules.Customization;

/// <summary>
/// Catalogue des réglages de personnalisation. Chaque valeur de registre correspond à ce qu'écrit l'interface de
/// Windows (Paramètres, Options des dossiers, Panneau de configuration) ou à une stratégie de groupe documentée.
/// Les rares astuces non documentées par Microsoft (menu contextuel classique, suffixe des raccourcis, volet de
/// navigation) sont signalées comme telles dans leur description.
/// </summary>
internal static class CustomizationTweaks
{
    private const string C = CustomizationModule.Category;

    // Groupes (sous-titres de la page, dans cet ordre).
    public static readonly string GroupColors = L("Colors");
    public static readonly string GroupTaskbar = L("Taskbar");
    public static readonly string GroupStart = L("Start menu");
    public static readonly string GroupExplorer = L("File Explorer");
    public static readonly string GroupDesktop = L("Desktop and windows");
    public static readonly string GroupLogon = L("Sign-in and lock");
    public static readonly string GroupInput = L("Mouse and keyboard");

    // Clés de registre (HKCU sauf mention contraire).
    internal const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string Dwm = @"Software\Microsoft\Windows\DWM";
    private const string Adv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string TaskbarDev = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings";
    private const string Explorer = @"Software\Microsoft\Windows\CurrentVersion\Explorer";
    private const string Search = @"Software\Microsoft\Windows\CurrentVersion\Search";
    private const string CabinetState = @"Software\Microsoft\Windows\CurrentVersion\Explorer\CabinetState";
    private const string NamingTemplates = @"Software\Microsoft\Windows\CurrentVersion\Explorer\NamingTemplates";
    private const string DesktopIcons = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";
    internal const string DesktopKey = @"Control Panel\Desktop";
    private const string MouseKey = @"Control Panel\Mouse";
    private const string StickyKeys = @"Control Panel\Accessibility\StickyKeys";
    private const string FilterKeys = @"Control Panel\Accessibility\Keyboard Response";
    private const string ToggleKeys = @"Control Panel\Accessibility\ToggleKeys";
    private const string ClassicMenuClsid = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
    private const string GalleryClsid = @"Software\Classes\CLSID\{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}";
    private const string HomeClsid = @"Software\Classes\CLSID\{f874310e-b6b7-47dc-bc84-b9e6b38f5903}";
    private const string PinnedToNavPane = "System.IsPinnedToNameSpaceTree";

    // HKLM / HKU\.DEFAULT (admin).
    private const string ExplorerPolicy = @"SOFTWARE\Policies\Microsoft\Windows\Explorer";
    private const string SystemPolicy = @"SOFTWARE\Policies\Microsoft\Windows\System";
    private const string SystemPoliciesLegacy = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string BootAnimation = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI\BootAnimation";
    private const string DefaultKeyboard = @"Control Panel\Keyboard";

    private const string On = TweakDefinition.On;
    private const string Off = TweakDefinition.Off;

    /// <summary>Notification envoyée après un changement de barre des tâches (rafraîchit Explorer quand il le permet).</summary>
    private static BroadcastSettingChangeOp Tray => Sys.Broadcast("TraySettings");
    private static BroadcastSettingChangeOp Colors => Sys.Broadcast("ImmersiveColorSet");

    public static IEnumerable<TweakDefinition> All() =>
    [
        .. ColorTweaks(),
        .. TaskbarTweaks(),
        .. StartTweaks(),
        .. ExplorerTweaks(),
        .. DesktopTweaks(),
        .. LogonTweaks(),
        .. InputTweaks(),
    ];

    /// <summary>Réglages affichés dans la section « Apparence » de la page (et non dans la liste principale).</summary>
    public static readonly string[] AppearanceIds =
    [
        "custom.theme.apps", "custom.theme.system", "custom.colors.autoaccent",
        "custom.colors.accentstart", "custom.colors.accenttitlebars", "custom.colors.transparency",
    ];

    // =====================================================================================================
    // Couleurs
    // =====================================================================================================

    private static IEnumerable<TweakDefinition> ColorTweaks()
    {
        yield return Tweak.Choice("custom.theme.apps", L("App mode"),
                L("Light or dark for apps: Settings, File Explorer, modern apps. Some older software ignores this setting."))
            .In(C, GroupColors)
            .Keywords(L("theme, dark, light, dark mode, light mode, night mode, apps"))
            .Option("light", L("Light"), Reg.CuDword(Personalize, "AppsUseLightTheme", 1), Colors)
            .Option("dark", L("Dark"), Reg.CuDword(Personalize, "AppsUseLightTheme", 0), Colors)
            .WindowsDefault("light")
            .Build();

        yield return Tweak.Choice("custom.theme.system", L("Windows mode"),
                L("Colors of the taskbar, Start menu and notification center."))
            .In(C, GroupColors)
            .Keywords(L("theme, dark, light, taskbar, dark mode, start menu"))
            .Option("light", L("Light"), Reg.CuDword(Personalize, "SystemUsesLightTheme", 1), Colors)
            .Option("dark", L("Dark"), Reg.CuDword(Personalize, "SystemUsesLightTheme", 0), Colors)
            // Absent du registre : Windows 11 est clair par défaut, Windows 10 sombre.
            .Detect(() => RegistryAccess.ReadDword(RegHive.CurrentUser, Personalize, "SystemUsesLightTheme") switch
            {
                1 => "light",
                0 => "dark",
                _ => OsInfo.Build >= 22000 ? "light" : "dark",
            })
            .Build();

        yield return Tweak.Toggle("custom.colors.autoaccent", L("Automatic accent color"),
                L("Windows picks the accent color from your wallpaper and keeps it in sync. The color is recalculated the next time the wallpaper changes (or the next time you sign in)."))
            .In(C, GroupColors)
            .Keywords(L("accent, color, wallpaper, automatic, accent color"))
            .WhenOn(Reg.CuDword(DesktopKey, "AutoColorization", 1), Colors)
            .WhenOff(Reg.CuDword(DesktopKey, "AutoColorization", 0), Colors)
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Toggle("custom.colors.accentstart", L("Accent color on Start and taskbar"),
                L("Colors the Start menu, taskbar and notification center with the accent color. On Windows 11, this only works when the Windows mode is dark."))
            .In(C, GroupColors)
            .Keywords(L("accent, color, taskbar, start menu, taskbar color"))
            .WhenOn(Reg.CuDword(Personalize, "ColorPrevalence", 1), Colors)
            .WhenOff(Reg.CuDword(Personalize, "ColorPrevalence", 0), Colors)
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Toggle("custom.colors.accenttitlebars", L("Accent color on title bars"),
                L("Shows the accent color on window title bars and borders. If the change doesn't appear right away, you'll see it the next time you sign in."))
            .In(C, GroupColors)
            .Keywords(L("accent, title bar, border, window, window color"))
            .WhenOn(Reg.CuDword(Dwm, "ColorPrevalence", 1), Colors)
            .WhenOff(Reg.CuDword(Dwm, "ColorPrevalence", 0), Colors)
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Toggle("custom.colors.transparency", L("Transparency effects"),
                L("Translucent effect (Mica, acrylic) on the taskbar, Start menu and some windows. Turning it off slightly lightens the load on low-end PCs."))
            .In(C, GroupColors)
            .Keywords(L("transparency, acrylic, mica, blur, translucent"))
            .Tags("lowend", "battery")
            .WhenOn(Reg.CuDword(Personalize, "EnableTransparency", 1), Colors)
            .WhenOff(Reg.CuDword(Personalize, "EnableTransparency", 0), Colors)
            .WindowsDefault(On)
            .RecommendWhen(p => p.HardwareLoaded && p.Tier == PerformanceTier.Low ? Off : null)
            .Build();
    }

    // =====================================================================================================
    // Barre des tâches
    // =====================================================================================================

    private static IEnumerable<TweakDefinition> TaskbarTweaks()
    {
        yield return Tweak.Choice("custom.taskbar.alignment", L("Taskbar alignment"),
                L("Position of the Start button and pinned apps: centered (Windows 11) or on the left as in Windows 10."))
            .In(C, GroupTaskbar)
            .Keywords(L("alignment, left, center, icons, taskbar alignment, start button"))
            .Requires(Requires.Windows11)
            .Option("left", L("Left"), Reg.CuDword(Adv, "TaskbarAl", 0), Tray)
            .Option("center", LC("taskbar alignment", "Center"), Reg.CuDword(Adv, "TaskbarAl", 1), Tray)
            .WindowsDefault("center")
            .Build();

        yield return Tweak.Choice("custom.taskbar.search", L("Search on the taskbar"),
                L("How Windows Search appears on the taskbar. Even when hidden, search is still available: press the Windows key and type what you're looking for."))
            .In(C, GroupTaskbar)
            .Keywords(L("search, magnifying glass, search box, search icon, hide search"))
            .Requires(Requires.Windows11_22H2)
            .Option("hidden", LC("feminine", "Hidden"), Reg.CuDword(Search, "SearchboxTaskbarMode", 0), Tray)
            .Option("icon", L("Icon only"), Reg.CuDword(Search, "SearchboxTaskbarMode", 1), Tray)
            .Option("iconlabel", L("Icon and label"), Reg.CuDword(Search, "SearchboxTaskbarMode", 3), Tray)
            .Option("box", L("Search box"), Reg.CuDword(Search, "SearchboxTaskbarMode", 2), Tray)
            .WindowsDefault("box")
            .Build();

        yield return Tweak.Choice("custom.taskbar.search.win10", L("Search on the taskbar (Windows 10)"),
                L("How Windows Search appears on the Windows 10 taskbar. Even when hidden, search is still available: press the Windows key and type what you're looking for."))
            .In(C, GroupTaskbar)
            .Keywords(L("search, magnifying glass, search box, cortana"))
            .Requires(Requires.Windows10Only)
            .Option("hidden", LC("feminine", "Hidden"), Reg.CuDword(Search, "SearchboxTaskbarMode", 0), Tray)
            .Option("icon", L("Icon only"), Reg.CuDword(Search, "SearchboxTaskbarMode", 1), Tray)
            .Option("box", L("Search box"), Reg.CuDword(Search, "SearchboxTaskbarMode", 2), Tray)
            .WindowsDefault("box")
            .Build();

        yield return Tweak.Toggle("custom.taskbar.taskview", L("Task view button"),
                L("Button that shows open windows and virtual desktops. The Windows + Tab shortcut is still available."))
            .In(C, GroupTaskbar)
            .Keywords(L("task view, virtual desktops, virtual desktop, timeline"))
            .WhenOn(Reg.CuDword(Adv, "ShowTaskViewButton", 1), Tray)
            .WhenOff(Reg.CuDword(Adv, "ShowTaskViewButton", 0), Tray)
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("custom.taskbar.widgets", L("Widgets button"),
                L("Weather and news button on the taskbar (only the button is affected: Windows + W is still available). On some recent versions, Windows protects this setting and may prevent apps other than Settings from changing it: in that case, use Settings › Personalization › Taskbar."))
            .In(C, GroupTaskbar)
            .Keywords(L("widgets, weather, news, widgets button"))
            .Tags("family", "office")
            .Requires(Requires.Windows11)
            .WhenOn(Reg.CuDword(Adv, "TaskbarDa", 1), Tray)
            .WhenOff(Reg.CuDword(Adv, "TaskbarDa", 0), Tray)
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("custom.taskbar.chat", L("Chat button (Microsoft Teams)"),
                L("Chat button in Windows 11 21H2 and 22H2, which opens the personal version of Microsoft Teams."))
            .In(C, GroupTaskbar)
            .Keywords(L("chat, teams, teams button, conversation, messaging"))
            .Tags("family", "office")
            .Requires(Requires.When(p => p.Build is >= 22000 and < 22631,
                L("The Chat button only exists on Windows 11 21H2 and 22H2.")))
            .WhenOn(Reg.CuDword(Adv, "TaskbarMn", 1), Tray)
            .WhenOff(Reg.CuDword(Adv, "TaskbarMn", 0), Tray)
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("custom.taskbar.copilot", L("Copilot button"),
                L("Copilot button on the Windows 11 22H2 and 23H2 taskbar. Only the button is affected: turning off Copilot itself is on the Privacy page."))
            .In(C, GroupTaskbar)
            .Keywords(L("copilot, ai, assistant, copilot button"))
            .Requires(Requires.When(p => p.Build is >= 22621 and < 26100,
                L("Since Windows 11 24H2, Copilot is an app: unpin it from the taskbar.")))
            .WhenOn(Reg.CuDword(Adv, "ShowCopilotButton", 1), Tray)
            .WhenOff(Reg.CuDword(Adv, "ShowCopilotButton", 0), Tray)
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("custom.taskbar.endtask", L("“End task” on right-click"),
                L("Adds “End task” to the right-click menu of apps on the taskbar, to force-close a frozen app without opening Task Manager. Unsaved data is lost."))
            .In(C, GroupTaskbar)
            .Keywords(L("end task, force close, force quit, frozen app, not responding, kill"))
            .Tags("dev", "office")
            .Requires(Requires.When(p => p.Build >= 22631, L("Requires Windows 11 23H2 or later.")))
            .WhenOn(Reg.CuDword(TaskbarDev, "TaskbarEndTask", 1), Tray)
            .WhenOff(Reg.CuDword(TaskbarDev, "TaskbarEndTask", 0), Tray)
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Toggle("custom.taskbar.seconds", L("Seconds in the clock"),
                L("Shows seconds in the taskbar clock. Microsoft says this uses a little more power."))
            .In(C, GroupTaskbar)
            .Keywords(L("seconds, clock, time"))
            .Tags("dev")
            .Requires(Requires.When(p => p.Build < 22000 || p.Build >= 22621,
                L("On Windows 11, showing seconds requires version 22H2 or later.")))
            .Effect(ApplyEffect.RestartExplorer)
            .WhenOn(Reg.CuDword(Adv, "ShowSecondsInSystemClock", 1), Tray)
            .WhenOff(Reg.CuDword(Adv, "ShowSecondsInSystemClock", 0), Tray)
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Choice("custom.taskbar.combine", L("Button combining"),
                L("Groups windows of the same app under a single button, with or without hiding their labels."))
            .In(C, GroupTaskbar)
            .Keywords(L("group, combine, labels, never combine, taskbar buttons"))
            .Tags("office")
            .Requires(Requires.When(p => p.Build < 22000 || p.Build >= 22631,
                L("On Windows 11, this option is only available from version 23H2.")))
            .Option("always", L("Always, hide labels"), Reg.CuDword(Adv, "TaskbarGlomLevel", 0), Tray)
            .Option("whenfull", L("When taskbar is full"), Reg.CuDword(Adv, "TaskbarGlomLevel", 1), Tray)
            .Option("never", L("Never"), Reg.CuDword(Adv, "TaskbarGlomLevel", 2), Tray)
            .WindowsDefault("always")
            .Build();

        yield return Tweak.Toggle("custom.taskbar.badges", L("Badges on apps"),
                L("Notification badges (unread messages, etc.) on taskbar icons."))
            .In(C, GroupTaskbar)
            .Keywords(L("badge, counter, unread, notification"))
            .WhenOn(Reg.CuDword(Adv, "TaskbarBadges", 1), Tray)
            .WhenOff(Reg.CuDword(Adv, "TaskbarBadges", 0), Tray)
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("custom.taskbar.flashing", L("Flashing apps"),
                L("An app that needs your attention flashes its button on the taskbar."))
            .In(C, GroupTaskbar)
            .Keywords(L("flash, flashing, blink, attention"))
            .Requires(Requires.Windows11_22H2)
            .WhenOn(Reg.CuDword(Adv, "TaskbarFlashing", 1), Tray)
            .WhenOff(Reg.CuDword(Adv, "TaskbarFlashing", 0), Tray)
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("custom.taskbar.multimonitor", L("Taskbar on all displays"),
                L("With multiple displays, shows a taskbar on each of them (otherwise only on the main display)."))
            .In(C, GroupTaskbar)
            .Keywords(L("multiple displays, multi-monitor, second screen, dual screen, monitor"))
            .Effect(ApplyEffect.RestartExplorer)
            .WhenOn(Reg.CuDword(Adv, "MMTaskbarEnabled", 1), Tray)
            .WhenOff(Reg.CuDword(Adv, "MMTaskbarEnabled", 0), Tray)
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Choice("custom.taskbar.multimonitor.buttons", L("Window buttons with multiple displays"),
                L("Taskbars where buttons for open windows appear, when the taskbar is shown on all displays."))
            .In(C, GroupTaskbar)
            .Keywords(L("multiple displays, multi-monitor, second screen, buttons"))
            .Effect(ApplyEffect.RestartExplorer)
            .Option("all", L("All taskbars"), Reg.CuDword(Adv, "MMTaskbarMode", 0), Tray)
            .Option("mainandwhere", L("Main taskbar and taskbar where window is open"), Reg.CuDword(Adv, "MMTaskbarMode", 1), Tray)
            .Option("where", L("Taskbar where window is open"), Reg.CuDword(Adv, "MMTaskbarMode", 2), Tray)
            .WindowsDefault("all")
            .Build();

        yield return Tweak.Toggle("custom.taskbar.showdesktop", L("“Show desktop” corner"),
                L("Clicking the far right end of the taskbar minimizes all windows to show the desktop."))
            .In(C, GroupTaskbar)
            .Keywords(L("show desktop, corner, minimize all, peek"))
            .Requires(Requires.Windows11_22H2)
            .WhenOn(Reg.CuDword(Adv, "TaskbarSd", 1), Tray)
            .WhenOff(Reg.CuDword(Adv, "TaskbarSd", 0), Tray)
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("custom.taskbar.smallicons", L("Small taskbar buttons"),
                L("Reduces the height of the Windows 10 taskbar by using small icons."))
            .In(C, GroupTaskbar)
            .Keywords(L("small icons, size, height, compact"))
            .Requires(Requires.Windows10Only)
            .Effect(ApplyEffect.RestartExplorer)
            .WhenOn(Reg.CuDword(Adv, "TaskbarSmallIcons", 1), Tray)
            .WhenOff(Reg.CuDword(Adv, "TaskbarSmallIcons", 0), Tray)
            .WindowsDefault(Off)
            .Build();
    }

    // =====================================================================================================
    // Menu Démarrer
    // =====================================================================================================

    private static IEnumerable<TweakDefinition> StartTweaks()
    {
        yield return Tweak.Choice("custom.start.layout", L("Start menu layout"),
                L("How space is split between pinned apps and the “Recommended” section. May have no effect on the redesigned Start menu released in late 2025, which no longer has this option."))
            .In(C, GroupStart)
            .Keywords(L("start menu, pins, pinned, recommended, layout, start layout, more pins"))
            .Requires(Requires.Windows11_22H2)
            .Effect(ApplyEffect.SignOut)
            .Option("default", L("Default"), Reg.CuDword(Adv, "Start_Layout", 0))
            .Option("pins", L("More pins"), Reg.CuDword(Adv, "Start_Layout", 1))
            .Option("recommendations", L("More recommendations"), Reg.CuDword(Adv, "Start_Layout", 2))
            .WindowsDefault("default")
            .Build();

        yield return Tweak.Choice("custom.start.mostused", L("“Most used” list in Start"),
                L("Forces showing or hiding the most used apps in the Start menu for all users of the PC (Windows policy). “Per Settings” lets each person choose."))
            .In(C, GroupStart)
            .Keywords(L("most used, frequent apps, start menu, list"))
            .Requires(Requires.Windows11_22H2)
            .Effect(ApplyEffect.SignOut)
            .Option("user", L("Per Settings"), Reg.LmDel(ExplorerPolicy, "ShowOrHideMostUsedApps"))
            .Option("show", L("Always shown"), Reg.LmDword(ExplorerPolicy, "ShowOrHideMostUsedApps", 1))
            .Option("hide", L("Always hidden"), Reg.LmDword(ExplorerPolicy, "ShowOrHideMostUsedApps", 2))
            .WindowsDefault("user")
            .Build();

        yield return Tweak.Toggle("custom.start.recentlyadded", L("Recently added apps in Start"),
                L("List of recently installed apps in the Start menu. “Hidden” enforces it for all users (Windows policy); “Allowed” leaves the choice in Settings."))
            .In(C, GroupStart)
            .Keywords(L("recently added, new apps, start menu"))
            .Labels(LC("feminine plural", "Allowed"), LC("feminine plural", "Hidden"))
            .Effect(ApplyEffect.SignOut)
            .WhenOn(Reg.LmDel(ExplorerPolicy, "HideRecentlyAddedApps"))
            .WhenOff(Reg.LmDword(ExplorerPolicy, "HideRecentlyAddedApps", 1))
            .WindowsDefault(On)
            .Build();
    }

    // =====================================================================================================
    // Explorateur de fichiers
    // =====================================================================================================

    private static IEnumerable<TweakDefinition> ExplorerTweaks()
    {
        yield return Tweak.Toggle("custom.explorer.extensions", L("Show file name extensions"),
                L("Shows “.pdf”, “.exe”… at the end of file names. Recommended: helps you spot booby-trapped files (e.g. “invoice.pdf.exe”)."))
            .In(C, GroupExplorer)
            .Keywords(L("extension, file, exe, file type, file extension"))
            .Tags("security", "office")
            .WhenOn(Reg.CuDword(Adv, "HideFileExt", 0))
            .WhenOff(Reg.CuDword(Adv, "HideFileExt", 1))
            .WindowsDefault(Off)
            .Recommend(On)
            .Effect(ApplyEffect.RestartExplorer)
            .Build();

        yield return Tweak.Toggle("custom.explorer.hidden", L("Hidden files and folders"),
                L("Shows items marked “hidden” (AppData, configuration folders…), semi-transparent. Press F5 in windows that are already open."))
            .In(C, GroupExplorer)
            .Keywords(L("hidden files, appdata, show hidden, invisible"))
            .Tags("dev")
            .WhenOn(Reg.CuDword(Adv, "Hidden", 1))
            .WhenOff(Reg.CuDword(Adv, "Hidden", 2))
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Toggle("custom.explorer.superhidden", L("Protected operating system files"),
                L("Shows protected system files (desktop.ini, pagefile.sys…). Not needed day to day. Press F5 in windows that are already open."))
            .In(C, GroupExplorer)
            .Keywords(L("system files, protected, super hidden, desktop.ini, system"))
            .Tags("dev")
            .Risk(RiskLevel.Advanced)
            .Warning(L("Deleting or modifying these files can stop Windows from working or starting."))
            .WhenOn(Reg.CuDword(Adv, "ShowSuperHidden", 1))
            .WhenOff(Reg.CuDword(Adv, "ShowSuperHidden", 0))
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Choice("custom.explorer.launchto", L("Open File Explorer to"),
                L("Page shown when File Explorer opens (Windows + E)."))
            .In(C, GroupExplorer)
            .Keywords(L("this pc, quick access, home, open to, my computer"))
            .Tags("office")
            .Option("home", L("Home (Quick access)"), Reg.CuDword(Adv, "LaunchTo", 2))
            .Option("thispc", L("This PC"), Reg.CuDword(Adv, "LaunchTo", 1))
            .WindowsDefault("home")
            .Build();

        yield return Tweak.Toggle("custom.explorer.compact", L("Compact view"),
                L("Reduces the spacing between items in File Explorer, which Windows 11 enlarges for touch. Applies to new windows."))
            .In(C, GroupExplorer)
            .Keywords(L("compact, spacing, density, compact view, padding"))
            .Requires(Requires.Windows11)
            .WhenOn(Reg.CuDword(Adv, "UseCompactMode", 1))
            .WhenOff(Reg.CuDword(Adv, "UseCompactMode", 0))
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Toggle("custom.explorer.checkboxes", L("Item check boxes"),
                L("Adds a check box to each file so you can select several without holding Ctrl: handy with a finger or a touchpad."))
            .In(C, GroupExplorer)
            .Keywords(L("check boxes, checkbox, selection, touch, select"))
            .WhenOn(Reg.CuDword(Adv, "AutoCheckSelect", 1))
            .WhenOff(Reg.CuDword(Adv, "AutoCheckSelect", 0))
            .WindowsDefault(Off)
            .RecommendWhen(p => p.HardwareLoaded && p.HasTouch ? On : null)
            .Build();

        yield return Tweak.Toggle("custom.explorer.fullpath", L("Full path in the title bar"),
                L("Shows the folder's full path (e.g. C:\\Users\\…\\Documents) in the window or tab title."))
            .In(C, GroupExplorer)
            .Keywords(L("full path, title bar, address, title"))
            .Tags("dev", "office")
            .WhenOn(Reg.CuDword(CabinetState, "FullPath", 1))
            .WhenOff(Reg.CuDword(CabinetState, "FullPath", 0))
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Toggle("custom.explorer.syncnotifications", L("Sync provider notifications"),
                L("Messages from OneDrive or other sync services shown in File Explorer, often subscription or feature suggestions."))
            .In(C, GroupExplorer)
            .Keywords(L("onedrive, notifications, sync, sync provider, explorer ads"))
            .WhenOn(Reg.CuDword(Adv, "ShowSyncProviderNotifications", 1))
            .WhenOff(Reg.CuDword(Adv, "ShowSyncProviderNotifications", 0))
            .WindowsDefault(On)
            .Recommend(Off)
            .Build();

        yield return Tweak.Toggle("custom.explorer.classicmenu", L("Full context menu (Windows 10 style)"),
                L("Right-clicking shows all options right away, without going through “Show more options” (Shift + F10). A widespread tweak not documented by Microsoft: a Windows update could make it stop working."))
            .In(C, GroupExplorer)
            .Keywords(L("context menu, right-click, show more options, classic context menu, old menu, windows 10"))
            .Tags("office")
            .Requires(Requires.Windows11)
            .Effect(ApplyEffect.RestartExplorer)
            // La clé est d'abord remise à zéro : l'annulation supprime ainsi toute la clé CLSID (sinon une clé vide resterait).
            .WhenOn(Reg.CuDelKey(ClassicMenuClsid), Reg.Cu(ClassicMenuClsid + @"\InprocServer32", "", RegistryValueKind.String, ""))
            .WhenOff(Reg.CuDelKey(ClassicMenuClsid))
            .Detect(() => RegistryAccess.KeyExists(RegHive.CurrentUser, ClassicMenuClsid + @"\InprocServer32") ? On : Off)
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Toggle("custom.explorer.gallery", L("Gallery in the navigation pane"),
                L("“Gallery” shortcut (a timeline view of your photos) in the left column of File Explorer. Method not documented by Microsoft, safe and can be undone."))
            .In(C, GroupExplorer)
            .Keywords(L("gallery, navigation pane, photos"))
            .Requires(Requires.When(p => p.Build >= 22631, L("Gallery only exists from Windows 11 23H2.")))
            .Effect(ApplyEffect.RestartExplorer)
            .WhenOn(Reg.CuDelKey(GalleryClsid))
            .WhenOff(Reg.CuDelKey(GalleryClsid), Reg.CuDword(GalleryClsid, PinnedToNavPane, 0))
            .Detect(() => NavPaneDetect(GalleryClsid))
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("custom.explorer.home", L("Home in the navigation pane"),
                L("“Home” shortcut (recent and favorite files) in the left column of File Explorer. If you hide it, choose “This PC” as the start page. Method not documented by Microsoft, can be undone."))
            .In(C, GroupExplorer)
            .Keywords(L("home, quick access, navigation pane"))
            .Requires(Requires.Windows11_22H2)
            .Effect(ApplyEffect.RestartExplorer)
            .WhenOn(Reg.CuDelKey(HomeClsid))
            .WhenOff(Reg.CuDelKey(HomeClsid), Reg.CuDword(HomeClsid, PinnedToNavPane, 0))
            .Detect(() => NavPaneDetect(HomeClsid))
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Choice("custom.explorer.driveletters", L("Drive letters"),
                L("Position of the letter (C:, D:…) in drive names shown by File Explorer."))
            .In(C, GroupExplorer)
            .Keywords(L("drive letter, c:, disk, drive"))
            .Effect(ApplyEffect.RestartExplorer)
            .Option("after", L("After the name"), Reg.CuDel(Explorer, "ShowDriveLettersFirst"))
            .Option("before", L("Before the name"), Reg.CuDword(Explorer, "ShowDriveLettersFirst", 4))
            .Option("network", L("Before the name (network drives only)"), Reg.CuDword(Explorer, "ShowDriveLettersFirst", 1))
            .Option("hidden", LC("feminine plural", "Hidden"), Reg.CuDword(Explorer, "ShowDriveLettersFirst", 2))
            .WindowsDefault("after")
            .Build();

        yield return Tweak.Toggle("custom.explorer.shortcutsuffix", L("“ - Shortcut” suffix on new shortcuts"),
                L("Windows adds “ - Shortcut” to the name of shortcuts you create. When off, the shortcut simply has the item's name. Existing shortcuts aren't renamed."))
            .In(C, GroupExplorer)
            .Keywords(L("shortcut, suffix, - shortcut, shortcut name"))
            .Effect(ApplyEffect.RestartExplorer)
            .WhenOn(Reg.CuDel(NamingTemplates, "ShortcutNameTemplate"))
            .WhenOff(Reg.CuString(NamingTemplates, "ShortcutNameTemplate", "\"%s.lnk\""))
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("custom.explorer.statusbar", L("Status bar"),
                L("Line at the bottom of File Explorer windows showing the number of items and the size of the selection."))
            .In(C, GroupExplorer)
            .Keywords(L("status bar, item count, number of items, selection"))
            .WhenOn(Reg.CuDword(Adv, "ShowStatusBar", 1))
            .WhenOff(Reg.CuDword(Adv, "ShowStatusBar", 0))
            .WindowsDefault(On)
            .Build();
    }

    // =====================================================================================================
    // Bureau et fenêtres
    // =====================================================================================================

    private static IEnumerable<TweakDefinition> DesktopTweaks()
    {
        yield return DesktopIcon("custom.desktop.thispc", L("“This PC” icon on the desktop"),
            L("Shortcut to the computer's drives and folders."),
            "{20D04FE0-3AEA-1069-A2D8-08002B30309D}", visibleByDefault: false, L("this pc, my computer, computer"));
        yield return DesktopIcon("custom.desktop.recyclebin", L("“Recycle Bin” icon on the desktop"),
            L("Access to the Recycle Bin from the desktop (it's still available in File Explorer)."),
            "{645FF040-5081-101B-9F08-00AA002F954E}", visibleByDefault: true, L("recycle bin, trash, bin"));
        yield return DesktopIcon("custom.desktop.userfolder", L("User's files icon on the desktop"),
            L("Shortcut to your user folder (Documents, Pictures, Downloads…)."),
            "{59031a47-3f72-44a7-89c5-5595fe6b30ee}", visibleByDefault: false, L("user folder, user's files, profile, home folder"));
        yield return DesktopIcon("custom.desktop.network", L("“Network” icon on the desktop"),
            L("Shortcut to shared computers and devices on the local network."),
            "{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", visibleByDefault: false, L("network, network neighborhood"));
        yield return DesktopIcon("custom.desktop.controlpanel", L("“Control Panel” icon on the desktop"),
            L("Shortcut to the classic Control Panel."),
            "{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}", visibleByDefault: false, L("control panel"));

        yield return Tweak.Toggle("custom.windows.snap", L("Snap windows"),
                L("Dragging a window to an edge or corner of the screen resizes it (half or quarter of the screen). When off, snap layouts and Snap assist are unavailable too."))
            .In(C, GroupDesktop)
            .Keywords(L("snap, aero snap, side by side, resize, half screen, split screen"))
            .Effect(ApplyEffect.SignOut)
            .WhenOn(Reg.CuString(DesktopKey, "WindowArrangementActive", "1"))
            .WhenOff(Reg.CuString(DesktopKey, "WindowArrangementActive", "0"))
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("custom.windows.snaplayouts", L("Snap layouts on hover"),
                L("Hovering over a window's Maximize button suggests layouts (two columns, grid…). The Windows + Z shortcut is still available."))
            .In(C, GroupDesktop)
            .Keywords(L("snap layouts, layouts, maximize, hover, snap, grid"))
            .Requires(Requires.Windows11)
            .WhenOn(Reg.CuDword(Adv, "EnableSnapAssistFlyout", 1))
            .WhenOff(Reg.CuDword(Adv, "EnableSnapAssistFlyout", 0))
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("custom.windows.snapassist", L("Snap assist"),
                L("After you snap a window, Windows suggests your other open windows to fill the remaining space."))
            .In(C, GroupDesktop)
            .Keywords(L("snap assist, snap, window suggestions"))
            .WhenOn(Reg.CuDword(Adv, "SnapAssist", 1))
            .WhenOff(Reg.CuDword(Adv, "SnapAssist", 0))
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("custom.windows.shake", L("Shake to minimize other windows"),
                L("Shaking a window's title bar minimizes all the others (“Aero Shake”). Off by default on Windows 11, on by default on Windows 10."))
            .In(C, GroupDesktop)
            .Keywords(L("shake, aero shake, minimize windows, title bar"))
            .WhenOn(Reg.CuDword(Adv, "DisallowShaking", 0))
            .WhenOff(Reg.CuDword(Adv, "DisallowShaking", 1))
            .Detect(() => RegistryAccess.ReadDword(RegHive.CurrentUser, Adv, "DisallowShaking") switch
            {
                1 => Off,
                0 => On,
                _ => OsInfo.Build >= 22000 ? Off : On,
            })
            .Build();

        yield return Tweak.Choice("custom.windows.alttabtabs", L("Microsoft Edge tabs in Alt+Tab"),
                L("Tabs of compatible apps (Microsoft Edge) shown by Alt+Tab and window snapping. Depending on the Windows version, “All tabs” may be limited to the most recent ones."))
            .In(C, GroupDesktop)
            .Keywords(L("alt tab, tabs, edge, switch windows"))
            .Requires(Requires.When(p => p.Build >= 19042, L("Requires Windows 10 20H2 or later.")))
            .Option("default", L("Default setting"), Reg.CuDel(Adv, "MultiTaskingAltTabFilter"))
            .Option("none", L("Open windows only"), Reg.CuDword(Adv, "MultiTaskingAltTabFilter", 3))
            .Option("three", L("3 most recent tabs"), Reg.CuDword(Adv, "MultiTaskingAltTabFilter", 2))
            .Option("five", L("5 most recent tabs"), Reg.CuDword(Adv, "MultiTaskingAltTabFilter", 1))
            .Option("all", L("All tabs"), Reg.CuDword(Adv, "MultiTaskingAltTabFilter", 0))
            .WindowsDefault("default")
            .Build();

        yield return Tweak.Toggle("custom.desktop.jpegquality", L("Maximum JPEG wallpaper quality"),
                L("Windows recompresses JPEG wallpapers at 85% quality, which can cause visible artifacts. At 100%, the picture is kept with no visible loss. Applies the next time the wallpaper changes."))
            .In(C, GroupDesktop)
            .Keywords(L("wallpaper quality, jpeg, compression, artifacts, blurry wallpaper"))
            .WhenOn(Reg.CuDword(DesktopKey, "JPEGImportQuality", 100))
            .WhenOff(Reg.CuDel(DesktopKey, "JPEGImportQuality"))
            .WindowsDefault(Off)
            .Build();
    }

    private static TweakDefinition DesktopIcon(string id, string title, string description, string clsid, bool visibleByDefault, params string[] keywords) =>
        Tweak.Toggle(id, title, L("{0} Visible after refreshing the desktop (F5) or restarting Explorer.", description))
            .In(C, GroupDesktop)
            .Keywords([.. keywords, L("desktop icons, desktop")])
            .Labels(LC("feminine", "Shown"), LC("feminine", "Hidden"))
            .Effect(ApplyEffect.RestartExplorer)
            .WhenOn(Reg.CuDword(DesktopIcons, clsid, 0))
            .WhenOff(Reg.CuDword(DesktopIcons, clsid, 1))
            .WindowsDefault(visibleByDefault ? On : Off)
            .Build();

    // =====================================================================================================
    // Connexion et verrouillage
    // =====================================================================================================

    private static IEnumerable<TweakDefinition> LogonTweaks()
    {
        yield return Tweak.Toggle("custom.logon.acrylic", L("Blur effect on the sign-in screen"),
                L("Blur (acrylic) applied to the background picture behind the sign-in area. When off, the picture stays sharp. Windows policy: applies to all users."))
            .In(C, GroupLogon)
            .Keywords(L("blur, acrylic, sign-in screen, login, logon background"))
            .WhenOn(Reg.LmDel(SystemPolicy, "DisableAcrylicBackgroundOnLogon"))
            .WhenOff(Reg.LmDword(SystemPolicy, "DisableAcrylicBackgroundOnLogon", 1))
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("custom.logon.background", L("Background picture on the sign-in screen"),
                L("Shows the lock screen picture behind the sign-in area. When off, a solid color background is used. Applies to all users. This policy value isn't in Microsoft's administrative templates but is widely used: a Windows update could make it stop working."))
            .In(C, GroupLogon)
            .Keywords(L("sign-in screen, background picture, logon background, login background"))
            .WhenOn(Reg.LmDel(SystemPolicy, "DisableLogonBackgroundImage"))
            .WhenOff(Reg.LmDword(SystemPolicy, "DisableLogonBackgroundImage", 1))
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("custom.logon.verbose", L("Verbose status messages"),
                L("At startup, shutdown and sign-in, Windows shows the current step (“Applying Group Policy settings…”) instead of “Please wait”. Useful for diagnosing slowness."))
            .In(C, GroupLogon)
            .Keywords(L("verbose messages, verbose, startup diagnostics, please wait, slow startup, slow boot"))
            .Tags("dev")
            .Risk(RiskLevel.Moderate)
            .WhenOn(Reg.LmDword(SystemPoliciesLegacy, "VerboseStatus", 1))
            .WhenOff(Reg.LmDel(SystemPoliciesLegacy, "VerboseStatus"))
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Toggle("custom.logon.numlock", L("Num Lock on at startup"),
                L("Turns on the numeric keypad from the sign-in screen. On some PCs, the BIOS/UEFI setting takes precedence. On a laptop without a separate numeric keypad, some letters would then type numbers: don't turn this setting on."))
            .In(C, GroupLogon)
            .Keywords(L("num lock, numlock, numeric keypad, numbers"))
            .Tags("office")
            .WhenOn(Reg.DefString(DefaultKeyboard, "InitialKeyboardIndicators", "2147483650"))
            .WhenOff(Reg.DefString(DefaultKeyboard, "InitialKeyboardIndicators", "2147483648"))
            .Detect(() => ParseLong(RegistryAccess.Read(RegHive.DefaultUser, DefaultKeyboard, "InitialKeyboardIndicators")) is { } v
                ? ((v & 2) != 0 ? On : Off)
                : Off)
            .Build();

        yield return Tweak.Toggle("custom.logon.startupsound", L("Windows startup sound"),
                L("Sound played when the sign-in screen appears as the PC starts. Applies to all users."))
            .In(C, GroupLogon)
            .Keywords(L("startup sound, jingle, windows sound, boot sound"))
            .WhenOn(Reg.LmDword(BootAnimation, "DisableStartupSound", 0))
            .WhenOff(Reg.LmDword(BootAnimation, "DisableStartupSound", 1))
            // La stratégie « Désactiver le son de démarrage de Windows » (Logon.admx), si elle est définie, l'emporte.
            .Detect(() => (RegistryAccess.ReadDword(RegHive.LocalMachine, SystemPoliciesLegacy, "DisableStartupSound")
                           ?? RegistryAccess.ReadDword(RegHive.LocalMachine, BootAnimation, "DisableStartupSound")) switch
            {
                1 => Off,
                0 => On,
                _ => OsInfo.Build >= 22000 ? On : Off,
            })
            .Build();
    }

    // =====================================================================================================
    // Souris et clavier
    // =====================================================================================================

    private static IEnumerable<TweakDefinition> InputTweaks()
    {
        yield return Tweak.Toggle("custom.mouse.precision", L("Enhance pointer precision"),
                L("Mouse acceleration: the pointer travels farther when you move quickly. Many gamers and designers prefer to turn it off for proportional, predictable movement."))
            .In(C, GroupInput)
            .Keywords(L("mouse acceleration, pointer precision, enhance pointer precision, mouse, pointer"))
            .Tags("gaming")
            .Effect(ApplyEffect.SignOut)
            .WhenOn(Reg.CuString(MouseKey, "MouseSpeed", "1"), Reg.CuString(MouseKey, "MouseThreshold1", "6"), Reg.CuString(MouseKey, "MouseThreshold2", "10"))
            .WhenOff(Reg.CuString(MouseKey, "MouseSpeed", "0"), Reg.CuString(MouseKey, "MouseThreshold1", "0"), Reg.CuString(MouseKey, "MouseThreshold2", "0"))
            .WindowsDefault(On)
            .Build();

        yield return AccessibilityShortcut("custom.keyboard.stickykeys", L("Sticky keys shortcut (Shift × 5)"),
            L("Pressing Shift 5 times offers to turn on sticky keys."),
            StickyKeys, "510", "506", L("sticky keys, shift 5 times, shift"));
        yield return AccessibilityShortcut("custom.keyboard.filterkeys", L("Filter keys shortcut (right Shift 8 s)"),
            L("Holding the right Shift key for 8 seconds offers to turn on filter keys (brief or repeated keystrokes are ignored)."),
            FilterKeys, "126", "122", L("filter keys, right shift"));
        yield return AccessibilityShortcut("custom.keyboard.togglekeys", L("Toggle keys shortcut (Num Lock 5 s)"),
            L("Holding Num Lock for 5 seconds turns on toggle keys (a beep when you press Caps Lock, Num Lock and Scroll Lock)."),
            ToggleKeys, "62", "58", L("toggle keys, caps lock beep"));
    }

    /// <summary>
    /// Raccourci clavier d'une fonction d'accessibilité : valeurs « Flags » par défaut de Windows, avec ou sans le bit
    /// « raccourci actif » (0x4, *_HOTKEYACTIVE). La fonction elle-même reste disponible dans Paramètres › Accessibilité.
    /// </summary>
    private static TweakDefinition AccessibilityShortcut(string id, string title, string description, string key,
        string onValue, string offValue, params string[] keywords) =>
        Tweak.Toggle(id, title, L("{0} Turning off this shortcut prevents windows from opening by mistake (especially while gaming); the feature is still available in Settings › Accessibility. The feature's other options go back to their default values.", description))
            .In(C, GroupInput)
            .Keywords([.. keywords, L("accessibility, keyboard shortcut, keyboard")])
            .Tags("gaming")
            .Effect(ApplyEffect.SignOut)
            .WhenOn(Reg.CuString(key, "Flags", onValue))
            .WhenOff(Reg.CuString(key, "Flags", offValue))
            .Detect(() => ParseLong(RegistryAccess.Read(RegHive.CurrentUser, key, "Flags")) is { } v ? ((v & 0x4) != 0 ? On : Off) : On)
            .Build();

    /// <summary>
    /// Élément du volet de navigation masqué par une surcharge utilisateur (HKCU\Software\Classes\CLSID\{…}
    /// System.IsPinnedToNameSpaceTree = 0). « Masqué » remet d'abord la clé à zéro pour que l'annulation la supprime entièrement.
    /// </summary>
    private static string NavPaneDetect(string clsidKey) =>
        RegistryAccess.ReadDword(RegHive.CurrentUser, clsidKey, PinnedToNavPane) == 0 ? Off : On;

    private static long? ParseLong(object? value) => value switch
    {
        int i => i,
        long l => l,
        string s when long.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) => r,
        _ => null,
    };
}

/// <summary>Numéro de build de Windows (lecture unique, instantanée) pour les détections qui en dépendent.</summary>
internal static class OsInfo
{
    private static readonly Lazy<int> LazyBuild = new(() =>
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (int.TryParse(k?.GetValue("CurrentBuild") as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b)) return b;
        }
        catch { /* repli ci-dessous */ }
        return Environment.OSVersion.Version.Build;
    });

    public static int Build => LazyBuild.Value;
}
