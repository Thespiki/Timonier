namespace Timonier.Modules.WindowsTools;

/// <summary>Lien direct vers une page de l'application Paramètres (URI ms-settings: constante).</summary>
internal sealed record SettingLink(string Key, string Title, string Glyph, string[] Keywords)
{
    /// <summary>Page parente dans Paramètres (null = page de premier niveau de la section).</summary>
    public string? Parent { get; init; }
    /// <summary>Build minimal (22000 = Windows 11) ; en dessous, le lien n'est ni affiché ni indexé.</summary>
    public int MinBuild { get; init; }
    /// <summary>Faux si un autre module indexe déjà cette page dans la recherche (évite les doublons).</summary>
    public bool InSearch { get; init; } = true;
    public SettingsSection Section { get; set; } = null!;

    public string Uri => "ms-settings:" + Key;
    public string Id => "wintools.ms." + Key;
    public bool Windows11Only => MinBuild >= 22000;

    /// <summary>Chemin affiché : « Paramètres › Section › Page parente ».</summary>
    public string Path => Parent is null ? L("Settings › {0}", Section.Title) : L("Settings › {0} › {1}", Section.Title, Parent);

    public SettingLink Win11() => this with { MinBuild = Math.Max(MinBuild, 22000) };
    public SettingLink Min(int build) => this with { MinBuild = build };
    public SettingLink Under(string parent) => this with { Parent = parent };
    public SettingLink NoSearch() => this with { InSearch = false };
}

internal sealed class SettingsSection(string key, string title, string glyph, SettingLink[] links)
{
    public string Key { get; } = key;
    public string Title { get; } = title;
    public string Glyph { get; } = glyph;
    public IReadOnlyList<SettingLink> Links { get; } = links;
}

/// <summary>
/// Liens vers l'application Paramètres de Windows. Toutes les URI ont été vérifiées dans les ressources de
/// Windows 11 25H2 ; celles propres à Windows 11 (ou à une version précise) portent un build minimal.
/// </summary>
internal static class SettingsCatalog
{
    private static readonly Lazy<IReadOnlyList<SettingsSection>> AvailableSections = new(() =>
    {
        var build = WinInfo.Build;
        var list = new List<SettingsSection>();
        foreach (var s in All())
        {
            var links = s.Links.Where(l => l.MinBuild <= build).ToArray();
            if (links.Length == 0) continue;
            var section = new SettingsSection(s.Key, s.Title, s.Glyph, links);
            foreach (var l in links) l.Section = section;
            list.Add(section);
        }
        return list;
    });

    /// <summary>Sections et liens disponibles sur cette version de Windows.</summary>
    public static IReadOnlyList<SettingsSection> Sections => AvailableSections.Value;

    public static IEnumerable<SettingLink> Links => Sections.SelectMany(s => s.Links);

    private static SettingLink Link(string key, string title, string glyph, params string[] keywords) => new(key, title, glyph, keywords);

    private static IEnumerable<SettingsSection> All()
    {
        yield return new SettingsSection("system", L("System"), "",
        [
            Link("display", L("Display"), "", L("display, resolution, scaling, scale, multiple displays, monitors")).NoSearch(),
            Link("nightlight", L("Night light"), "", L("blue light, night light, blue light filter")).Under(L("Display")),
            Link("display-advanced", L("Advanced display"), "", L("refresh rate, hz, display information")).Under(L("Display")),
            Link("display-advancedgraphics", L("Graphics"), "", L("gpu per app, graphics preference")).Under(L("Display")).NoSearch(),
            Link("display-hdr", "HDR", "", L("hdr, high dynamic range, hdr video, auto hdr")).Under(L("Display")).Win11(),
            Link("sound", L("Sound"), "", L("audio, audio output, audio input, sound")),
            Link("sound-devices", L("All sound devices"), "", L("audio devices, speakers, headset, headphones, mic")).Under(L("Sound")),
            Link("apps-volume", L("Volume mixer"), "", L("per-app volume, volume mixer, per-app output")).Under(L("Sound")),
            Link("notifications", L("Notifications"), "", L("notifications, banners, do not disturb, notification center")),
            Link("quiethours", L("Focus"), "", L("focus assist, focus, focus session, timer")),
            Link("powersleep", L("Power"), "", L("power, sleep, screen off")).NoSearch(),
            Link("batterysaver", L("Energy saver"), "", L("battery saver, energy saver")).NoSearch(),
            Link("energyrecommendations", L("Energy recommendations"), "", L("energy saving, energy recommendations, carbon footprint")).Under(L("Power")).Min(22621),
            Link("storagesense", L("Storage"), "", L("disk space, storage")).NoSearch(),
            Link("storagepolicies", L("Storage Sense"), "", L("storage sense, automatic cleanup, empty recycle bin automatically")).Under(L("Storage")),
            Link("storagerecommendations", L("Cleanup recommendations"), "", L("free up space, large files, unused files")).Under(L("Storage")).Win11(),
            Link("disksandvolumes", L("Disks & volumes"), "", L("partitions, volumes, disk health, drive letter")).Under(L("Storage")).Win11(),
            Link("savelocations", L("Where new content is saved"), "", L("default location, save to another drive, save locations")).Under(L("Storage")),
            Link("multitasking", L("Multitasking"), "", L("snap windows, snap, alt tab, virtual desktops, title bar window shake")),
            Link("activation", L("Activation"), "", L("activate windows, product key, license, edition")),
            Link("troubleshoot", L("Troubleshoot"), "", L("troubleshooting, troubleshooters, troubleshooter")),
            Link("recovery", L("Recovery"), "", L("reset this pc, advanced startup")).NoSearch(),
            Link("project", L("Projecting to this PC"), "", L("miracast, project, cast, wireless display")),
            Link("remotedesktop", L("Remote Desktop"), "", L("rdp, remote desktop, remote control")),
            Link("clipboard", L("Clipboard"), "", L("clipboard history, windows v, clipboard, copy paste")),
            Link("crossdevice", L("Nearby sharing"), "", L("nearby sharing, send to nearby pc")).Win11(),
            Link("optionalfeatures", L("Optional features"), "", L("optional features, add a feature, rsat, openssh")),
            Link("systemcomponents", L("System components"), "", L("system components, system apps")).Min(26100),
            Link("developers", L("For developers"), "", L("developer mode, sudo, powershell execution, developer")),
            Link("about", L("System Information"), "", L("about, specifications, specs, pc name, windows version, rename this pc")),
        ]);

        yield return new SettingsSection("devices", L("Bluetooth & devices"), "",
        [
            Link("bluetooth", L("Bluetooth & devices"), "", L("bluetooth, pair, pairing")).NoSearch(),
            Link("devices", LC("Windows Settings page", "Devices"), "", L("add a device, connected devices, devices")),
            Link("printers", L("Printers & scanners"), "", L("printer, scanner")).NoSearch(),
            Link("mobile-devices", L("Mobile devices"), "", L("phone link, link to phone, android phone, iphone, smartphone")).Min(22631),
            Link("camera", L("Cameras"), "", L("webcam, camera settings, camera brightness, network cameras")).Win11(),
            Link("mousetouchpad", L("Mouse"), "", L("mouse, pointer speed")).NoSearch(),
            Link("devices-touchpad", L("Touchpad"), "", L("touchpad, gestures")).NoSearch(),
            Link("pen", L("Pen & Windows Ink"), "", L("pen, stylus, windows ink, handwriting, graphics tablet")),
            Link("autoplay", L("AutoPlay"), "", L("autoplay, usb drive inserted, memory card, sd card")),
            Link("usb", "USB", "", L("usb, usb notifications")).NoSearch(),
        ]);

        yield return new SettingsSection("network", L("Network & internet"), "",
        [
            Link("network-status", L("Network & internet"), "", L("network status")).NoSearch(),
            Link("network-wifi", "Wi-Fi", "", L("wifi, wi-fi, wireless")).NoSearch(),
            Link("network-wifisettings", L("Manage known networks"), "", L("known networks, forget a network, saved wifi, saved networks")).Under(L("Wi-Fi")),
            Link("network-ethernet", "Ethernet", "", L("network cable, rj45, ethernet, wired connection, metered connection")),
            Link("network-vpn", "VPN", "", L("vpn")).NoSearch(),
            Link("network-mobilehotspot", L("Mobile hotspot"), "", L("hotspot, mobile hotspot")).NoSearch(),
            Link("network-airplanemode", L("Airplane mode"), "", L("airplane mode, flight mode")).NoSearch(),
            Link("network-proxy", "Proxy", "", L("proxy")).NoSearch(),
            Link("network-dialup", L("Dial-up"), "", L("dial-up, pppoe, broadband connection, modem")),
            Link("network-advancedsettings", L("Advanced network settings"), "", L("network adapters, network reset, disable network adapter")).Win11(),
            Link("network-advancedsharing", L("Advanced sharing settings"), "", L("file sharing, network discovery, printer sharing")).Under(L("Advanced network settings")).Min(22621),
            Link("datausage", L("Data usage"), "", L("data usage, data consumption, data limit, metered connection")).Under(L("Advanced network settings")),
        ]);

        yield return new SettingsSection("personalization", L("Personalization"), "",
        [
            Link("personalization", L("Personalization"), "", L("personalize, appearance, personalization")),
            Link("personalization-background", L("Background"), "", L("wallpaper, background")).NoSearch(),
            Link("colors", L("Colors"), "", L("dark mode")).NoSearch(),
            Link("themes", L("Themes"), "", L("theme")).NoSearch(),
            Link("lockscreen", L("Lock screen"), "", LC("search keywords", "lock screen")).NoSearch(),
            Link("personalization-textinput", L("Text input"), "", L("touch keyboard, touch keyboard size, keyboard theme, input indicator")).Win11(),
            Link("personalization-start", LC("start menu", "Start"), "", L("start menu")).NoSearch(),
            Link("personalization-start-places", L("Folders"), "", L("folders next to power button, start menu shortcuts, downloads, settings in start")).Under(LC("start menu", "Start")).Win11(),
            Link("taskbar", L("Taskbar"), "", L("taskbar")).NoSearch(),
            Link("fonts", L("Fonts"), "", L("fonts")).NoSearch(),
            Link("deviceusage", L("Device usage"), "", L("device usage, gaming, school, creativity, personalized suggestions")).Win11(),
            Link("personalization-lighting", L("Dynamic Lighting"), "", L("rgb lighting, dynamic lighting, led, backlit keyboard")).Min(22631),
        ]);

        yield return new SettingsSection("apps", L("Apps"), "",
        [
            Link("appsfeatures", L("Installed apps"), "", L("installed apps")).NoSearch(),
            Link("advanced-apps", L("Advanced app settings"), "", L("install apps from anywhere, choose where to get apps, app execution aliases, archive apps")).Win11(),
            Link("defaultapps", L("Default apps"), "", L("default browser, open with, default apps, file associations, default pdf reader")),
            Link("startupapps", LC("settings page", "Startup"), "", L("startup apps")).NoSearch(),
            Link("maps", L("Offline maps"), "", L("maps, offline maps, downloaded maps")),
            Link("maps-downloadmaps", L("Download maps"), "", L("download a map, download maps")).Under(L("Offline maps")),
            Link("appsforwebsites", L("Apps for websites"), "", L("open links in app, apps for websites, web links")),
            Link("videoplayback", L("Video playback"), "", L("video, hdr video, video playback, video battery saving")),
        ]);

        yield return new SettingsSection("accounts", L("Accounts"), "",
        [
            Link("accounts", L("Accounts"), "", L("accounts, microsoft account")).Win11(),
            Link("yourinfo", L("Your info"), "", L("profile picture")).NoSearch(),
            Link("emailandaccounts", L("Email & accounts"), "", L("email accounts, accounts used by other apps, google account, outlook")),
            Link("signinoptions", L("Sign-in options"), "", L("windows hello")).NoSearch(),
            Link("signinoptions-dynamiclock", L("Dynamic lock"), "", L("dynamic lock, lock when I step away, bluetooth phone")).Under(L("Sign-in options")),
            Link("savedpasskeys", L("Passkeys"), "", L("passkey, passkeys, passwordless sign-in")).Min(26100),
            Link("family-group", L("Family"), "", L("family")).NoSearch(),
            Link("otherusers", L("Other users"), "", L("add a user, other users")).NoSearch(),
            Link("backup", L("Windows Backup"), "", L("backup, onedrive, back up my apps, remember my preferences")).Min(22621),
            Link("workplace", L("Access work or school"), "", L("work account, school account, company, intune, mdm, azure ad, entra")),
        ]);

        yield return new SettingsSection("time", L("Time & language"), "",
        [
            Link("dateandtime", L("Date & time"), "", L("time, time zone, sync time, set time automatically")),
            Link("regionlanguage", L("Language & region"), "", L("display language, add a language, keyboard layout, azerty, qwerty, country, regional format")),
            Link("typing", L("Typing"), "", L("autocorrect, text suggestions, touch typing, typing")),
            Link("speech", L("Speech"), "", L("speech recognition, text to speech, speech, microphone")),
        ]);

        yield return new SettingsSection("gaming", L("Games"), "",
        [
            Link("gaming-gamebar", "Game Bar", "", L("xbox game bar, game bar, windows g, controller, gamepad")),
            Link("gaming-gamedvr", L("Captures"), "", L("record a game, game capture, game dvr, captures folder, record what happened")),
            Link("gaming-gamemode", L("Game Mode"), "", L("game mode")).NoSearch(),
        ]);

        yield return new SettingsSection("accessibility", L("Accessibility"), "",
        [
            Link("easeofaccess-display", L("Text size"), "", L("enlarge text, bigger text, text size, larger font")),
            Link("easeofaccess-visualeffects", L("Visual effects"), "", L("always show scrollbars, transparency effects, animation effects, dismiss notifications")).Win11(),
            Link("easeofaccess-mousepointer", L("Mouse pointer and touch"), "", L("pointer size, pointer color, bigger cursor, mouse pointer")),
            Link("easeofaccess-cursor", L("Text cursor"), "", L("text cursor, text cursor indicator, cursor thickness")),
            Link("easeofaccess-magnifier", L("Magnifier"), "", L("magnifier, zoom, enlarge screen")),
            Link("easeofaccess-colorfilter", L("Color filters"), "", L("color blind, color blindness, grayscale, invert colors, color filter")),
            Link("easeofaccess-highcontrast", L("Contrast themes"), "", L("high contrast, contrast themes")),
            Link("easeofaccess-narrator", L("Narrator"), "", L("narrator, screen reader, natural voices")),
            Link("easeofaccess-audio", L("Audio"), "", L("mono audio, flash screen, visual notifications")),
            Link("easeofaccess-closedcaptioning", L("Captions"), "", L("subtitles, live captions, closed captions")),
            Link("easeofaccess-speechrecognition", L("Speech (accessibility)"), "", L("voice access, voice typing, windows speech recognition, voice commands")),
            Link("easeofaccess-keyboard", L("Keyboard"), "", L("sticky keys")).NoSearch(),
            Link("easeofaccess-mouse", L("Mouse"), "", L("mouse keys, numeric keypad mouse")),
            Link("easeofaccess-eyecontrol", L("Eye control"), "", L("eye control, eye tracking, gaze control")),
        ]);

        yield return new SettingsSection("privacy", L("Privacy & security"), "",
        [
            Link("windowsdefender", L("Windows Security"), "", L("windows security, antivirus, defender, virus protection")),
            Link("findmydevice", L("Find my device"), "", L("find my device, lost pc, stolen pc, locate my pc")),
            Link("deviceencryption", L("Device encryption"), "", L("device encryption, encrypt drive, bitlocker")),
            Link("privacy-general", L("General"), "", L("general privacy, advertising id, suggested content")).Under(L("Windows permissions")),
            Link("privacy-speech", L("Speech"), "", L("online speech recognition")).Under(L("Windows permissions")),
            Link("privacy-speechtyping", L("Inking & typing personalization"), "", L("personal dictionary, handwriting, inking and typing")).Under(L("Windows permissions")),
            Link("privacy-feedback", L("Diagnostics & feedback"), "", L("diagnostics")).Under(L("Windows permissions")).NoSearch(),
            Link("privacy-activityhistory", L("Activity history"), "", L("history, activity history")).Under(L("Windows permissions")).NoSearch(),
            Link("search-permissions", L("Search permissions"), "", L("safesearch, safe search, cloud content search, search history")).Under(L("Windows permissions")),
            Link("cortana-windowssearch", L("Searching Windows"), "", L("indexing, search my files, enhanced search, excluded locations, indexer")).Under(L("Windows permissions")),
            Link("privacy-location", L("Location"), "", L("location services, location, gps, app location")).Under(L("App permissions")),
            Link("privacy-webcam", L("Camera"), "", L("camera access, allow camera, webcam")).Under(L("App permissions")),
            Link("privacy-microphone", L("Microphone"), "", L("microphone access, allow microphone, mic")).Under(L("App permissions")),
            Link("privacy-voiceactivation", L("Voice activation"), "", L("wake word, voice activation, voice assistant")).Under(L("App permissions")),
            Link("privacy-notifications", L("Notifications"), "", L("notification access, read my notifications")).Under(L("App permissions")),
            Link("privacy-accountinfo", L("Account info"), "", L("name access, account picture, account info")).Under(L("App permissions")),
            Link("privacy-contacts", L("Contacts"), "", L("contacts access, address book")).Under(L("App permissions")),
            Link("privacy-calendar", L("Calendar"), "", L("calendar access, schedule")).Under(L("App permissions")),
            Link("privacy-phonecalls", L("Phone calls"), "", L("make calls, phone calls")).Under(L("App permissions")),
            Link("privacy-callhistory", L("Call history"), "", L("call log, call history")).Under(L("App permissions")),
            Link("privacy-email", L("Email"), "", L("email access, mail")).Under(L("App permissions")),
            Link("privacy-tasks", L("Tasks"), "", L("tasks access, to do")).Under(L("App permissions")),
            Link("privacy-messaging", L("Messaging"), "", L("sms, mms, text messages")).Under(L("App permissions")),
            Link("privacy-radios", L("Radios"), "", L("app control of bluetooth, radios, wifi control")).Under(L("App permissions")),
            Link("privacy-customdevices", LC("Windows Settings page", "Other devices"), "", L("unpaired devices, beacons")).Under(L("App permissions")),
            Link("privacy-appdiagnostics", L("App diagnostics"), "", L("app diagnostic info, app diagnostics")).Under(L("App permissions")),
            Link("privacy-automaticfiledownloads", L("Automatic file downloads"), "", L("automatic download, files on demand, onedrive")).Under(L("App permissions")),
            Link("privacy-documents", L("Documents"), "", L("documents access, documents library")).Under(L("App permissions")),
            Link("privacy-downloadsfolder", L("Downloads folder"), "", L("downloads access, downloads folder")).Under(L("App permissions")).Min(22621),
            Link("privacy-musiclibrary", L("Music library"), "", L("music access, music library")).Under(L("App permissions")),
            Link("privacy-pictures", L("Pictures"), "", L("pictures access, photos, pictures library")).Under(L("App permissions")),
            Link("privacy-videos", L("Videos"), "", L("videos access, videos library")).Under(L("App permissions")),
            Link("privacy-broadfilesystemaccess", L("File system"), "", L("file system access, all files, file system")).Under(L("App permissions")),
        ]);

        yield return new SettingsSection("update", "Windows Update", "",
        [
            Link("windowsupdate", L("Pause updates"), "", L("pause updates, pause, delay updates, postpone updates, windows update")),
            Link("windowsupdate-options", L("Advanced options"), "", L("windows update advanced options, updates for other microsoft products, restart, restart notifications")),
            Link("windowsupdate-history", L("Update history"), "", L("update history, kb, installed updates")),
            Link("windowsupdate-uninstallupdates", L("Uninstall updates"), "", L("uninstall an update, remove an update, uninstall update, kb")).Under(L("Update history")).Win11(),
            Link("windowsupdate-optionalupdates", L("Optional updates"), "", L("driver updates, optional drivers, optional updates")).Under(L("Advanced options")),
            Link("windowsupdate-activehours", L("Active hours"), "", L("active hours, don't restart during")).Under(L("Advanced options")),
            Link("delivery-optimization", L("Delivery Optimization"), "", L("delivery optimization, p2p, windows update bandwidth")).Under(L("Advanced options")),
            Link("windowsinsider", L("Windows Insider Program"), "", L("insider, preview, beta channel, dev channel, canary, preview builds")),
        ]);
    }
}
