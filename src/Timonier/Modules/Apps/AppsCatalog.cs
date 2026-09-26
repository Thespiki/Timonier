using Timonier.Core.Platform;

namespace Timonier.Modules.Apps;

/// <summary>
/// Application du catalogue d'installation (source « winget » communautaire).
/// <para><see cref="Tags"/> : essentials, office, gaming, dev, family, media, security, vendor, lowend (légère), heavy (exigeante).</para>
/// </summary>
public sealed record CatalogApp(string WingetId, string Name, string Category, string Description, string[] Tags)
{
    /// <summary>Constructeur du PC concerné (outils constructeur) : même libellé que <see cref="SystemProfile.Manufacturer"/>.</summary>
    public string? Manufacturer { get; init; }

    /// <summary>Fabricant de carte graphique concerné (pilotes graphiques).</summary>
    public HardwareVendor? GpuVendor { get; init; }

    /// <summary>Débuts possibles du nom affiché dans « Programmes et fonctionnalités » (détection « déjà installée »).</summary>
    public string[] Match { get; init; } = [];

    public bool HasTag(string tag) => Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);

    /// <summary>Vrai si ce programme semble déjà installé (comparaison avec les noms de la liste des programmes).</summary>
    public bool MatchesInstalledName(string displayName)
    {
        var prefixes = Match.Length > 0 ? Match : [Name];
        return prefixes.Any(p => displayName.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Pertinent pour ce PC (outil du constructeur, pilote de la carte graphique présente).</summary>
    public bool IsRelevantFor(SystemProfile profile) =>
        (Manufacturer is not null && string.Equals(Manufacturer, profile.Manufacturer, StringComparison.OrdinalIgnoreCase))
        || (GpuVendor is { } v && profile.Gpus.Any(g => g.Vendor == v));
}

/// <summary>
/// Catalogue d'applications installables en un clic (identifiants vérifiés dans la source « winget » communautaire).
/// Utilisé par la page Applications, l'action <c>apps.winget.install</c> (liste blanche) et les profils (étiquettes).
/// </summary>
public static class AppsCatalog
{
    // Noms affichés des catégories (déclarés avant Categories et All : initialisation statique dans l'ordre du texte).
    public static readonly string Browsers = L("Browsers");
    public static readonly string Office = L("Office & PDF");
    public static readonly string Media = L("Multimedia");
    public static readonly string Creation = L("Creative");
    public static readonly string Utilities = L("Utilities");
    public static readonly string Security = L("Security & passwords");
    public static readonly string Communication = L("Communication");
    public static readonly string Development = L("Development");
    public static readonly string Games = L("Games");
    public static readonly string Vendor = LC("app category", "Manufacturer tools");
    public static readonly string Drivers = L("Graphics drivers");
    public static readonly string Runtimes = L("Runtimes");

    /// <summary>Catégories dans l'ordre d'affichage, avec leur icône (Segoe Fluent Icons).</summary>
    public static IReadOnlyList<(string Name, string Glyph)> Categories { get; } =
    [
        (Browsers, ""), (Office, ""), (Media, ""), (Creation, ""), (Utilities, ""),
        (Security, ""), (Communication, ""), (Development, ""), (Games, ""),
        (Vendor, ""), (Drivers, ""), (Runtimes, ""),
    ];

    public static IReadOnlyList<CatalogApp> All { get; } = Build();

    private static readonly Dictionary<string, CatalogApp> ById =
        All.ToDictionary(a => a.WingetId, StringComparer.OrdinalIgnoreCase);

    public static CatalogApp? Find(string wingetId) => ById.GetValueOrDefault(wingetId);

    /// <summary>Identifiant présent dans le catalogue (comparaison insensible à la casse, comme winget).</summary>
    public static bool Contains(string wingetId) => ById.ContainsKey(wingetId);

    public static IEnumerable<CatalogApp> WithTag(string tag) => All.Where(a => a.HasTag(tag));

    public static string GlyphFor(string category) =>
        Categories.FirstOrDefault(c => c.Name == category).Glyph ?? "";

    private static CatalogApp A(string id, string name, string category, string description, params string[] tags) =>
        new(id, name, category, description, tags);

    private static List<CatalogApp> Build() =>
    [
        // ------------------------------------------------------------------ Navigateurs
        A("Mozilla.Firefox.fr", "Firefox", Browsers,
            L("Open-source browser from the Mozilla Foundation, focused on privacy. French-language version."), "essentials", "family")
            with { Match = ["Mozilla Firefox"] },
        A("Google.Chrome", "Google Chrome", Browsers,
            L("Google's browser, synced with your Google account."), "essentials"),
        A("Brave.Brave", "Brave", Browsers,
            L("Chromium-based browser with a built-in ad and tracker blocker."), "security"),
        A("Opera.Opera", "Opera", Browsers,
            L("Chromium-based browser with a built-in browser VPN and messaging apps in the sidebar."))
            with { Match = ["Opera Stable"] },
        A("Opera.OperaGX", "Opera GX", Browsers,
            L("Opera variant for gamers: adjustable memory, CPU, and bandwidth limits."), "gaming"),
        A("Vivaldi.Vivaldi", "Vivaldi", Browsers,
            L("Highly customizable browser: tab stacking, side panels, built-in email and calendar.")),
        A("LibreWolf.LibreWolf", "LibreWolf", Browsers,
            L("Privacy-focused Firefox variant: no telemetry, with uBlock Origin preinstalled."), "security"),

        // ------------------------------------------------------------------ Bureautique et PDF
        A("TheDocumentFoundation.LibreOffice", "LibreOffice", Office,
            L("Free, open-source office suite (documents, spreadsheets, presentations), compatible with Word, Excel, and PowerPoint files."),
            "essentials", "office", "family"),
        A("ONLYOFFICE.DesktopEditors", "ONLYOFFICE", Office,
            L("Free office suite with an interface close to Microsoft Office, very faithful to DOCX, XLSX, and PPTX formats."), "office"),
        A("SumatraPDF.SumatraPDF", "SumatraPDF", Office,
            L("Ultra-light, fast PDF, ePub, and comic book reader: ideal for low-end PCs."), "essentials", "office", "lowend"),
        A("Adobe.Acrobat.Reader.64-bit", "Adobe Acrobat Reader", Office,
            L("Adobe's PDF reader: read, annotate, fill in, and sign forms."), "office")
            with { Match = ["Adobe Acrobat"] },
        A("geeksoftwareGmbH.PDF24Creator", "PDF24 Creator", Office,
            L("Free, offline PDF toolbox: merge, compress, convert, PDF printer."), "office"),
        A("PDFsam.PDFsam", "PDFsam Basic", Office,
            L("Merge, split, extract, or rotate PDF pages, free and offline."), "office"),
        A("Obsidian.Obsidian", "Obsidian", Office,
            L("Markdown note-taking in local files, with links between notes."), "office"),
        A("Notion.Notion", "Notion", Office,
            L("Online workspace: notes, databases, wikis, and projects (account required)."), "office"),
        A("Joplin.Joplin", "Joplin", Office,
            L("Open-source, end-to-end encrypted note-taking app with sync (Nextcloud, Dropbox, OneDrive…)."), "office"),
        A("DigitalScholar.Zotero", "Zotero", Office,
            L("Reference manager for students and researchers."), "office", "family"),
        A("calibre.calibre", "calibre", Office,
            L("Manage an e-book library, convert formats, and send them to an e-reader."), "family"),
        A("Anki.Anki", "Anki", Office,
            L("Spaced-repetition flashcards for long-term memorization (languages, exams…)."), "family"),
        A("GeoGebra.Classic", "GeoGebra Classic", Office,
            L("Dynamic mathematics: geometry, functions, spreadsheet, and computer algebra. Widely used in middle and high school."), "family"),

        // ------------------------------------------------------------------ Multimédia
        A("VideoLAN.VLC", "VLC", Media,
            L("Free, open-source media player that plays almost every audio and video format, with no extra codecs."),
            "essentials", "media", "family")
            with { Match = ["VLC media player"] },
        A("clsid2.mpc-hc", "MPC-HC", Media,
            L("Light, fast video player, perfect for low-end PCs."), "media", "lowend")
            with { Match = ["MPC-HC"] },
        A("Daum.PotPlayer", "PotPlayer", Media,
            L("Full-featured video player with advanced hardware acceleration and many settings."), "media")
            with { Match = ["PotPlayer"] },
        A("CodecGuide.K-LiteCodecPack.Standard", "K-Lite Codec Pack Standard", Media,
            L("Codecs and filters so older players can play formats Windows doesn't support (includes MPC-HC)."), "media")
            with { Match = ["K-Lite"] },
        A("PeterPawlowski.foobar2000", "foobar2000", Media,
            L("Light, powerful audio player for large music libraries."), "media", "lowend"),
        A("AIMP.AIMP", "AIMP", Media,
            L("Free audio player with a polished interface, equalizer, and online radio."), "media"),
        A("Audacity.Audacity", "Audacity", Media,
            L("Record and edit audio: cut, mix, reduce noise."), "media"),
        A("HandBrake.HandBrake", "HandBrake", Media,
            L("Convert and compress videos (MP4, MKV) with simple presets."), "media"),
        A("OBSProject.OBSStudio", "OBS Studio", Media,
            L("Record your screen and stream live (Twitch, YouTube) with multiple scenes and sources."), "media", "gaming"),
        A("XBMCFoundation.Kodi", "Kodi", Media,
            L("Media center for movies, TV shows, music, and photos, ideal on a PC connected to a TV."), "media", "family"),
        A("MusicBrainz.Picard", "MusicBrainz Picard", Media,
            L("Automatically identify and tag your music files (titles, albums, cover art)."), "media"),
        A("IrfanSkiljan.IrfanView", "IrfanView", Media,
            L("Ultra-fast image viewer with simple editing and batch conversion."), "media", "lowend"),
        A("DuongDieuPhap.ImageGlass", "ImageGlass", Media,
            L("Modern, light image viewer that supports WebP, HEIC, AVIF, RAW…"), "media"),
        A("Stellarium.Stellarium", "Stellarium", Media,
            L("Planetarium: the real-time sky from your location, with stars, planets, and constellations."), "family"),

        // ------------------------------------------------------------------ Création
        A("GIMP.GIMP.3", "GIMP", Creation,
            L("Advanced photo editing and graphic design, an open-source alternative to Photoshop."), "media"),
        A("KDE.Krita", "Krita", Creation,
            L("Digital painting and illustration, popular with graphics tablet users."), "media", "family"),
        A("Inkscape.Inkscape", "Inkscape", Creation,
            L("Vector drawing (SVG): logos, illustrations, diagrams."), "media"),
        A("dotPDN.PaintDotNet", "Paint.NET", Creation,
            L("Simple, fast image editing, with layers and effects."), "media")
            with { Match = ["paint.net"] },
        A("darktable.darktable", "darktable", Creation,
            L("RAW photo processing and photo library, an open-source alternative to Lightroom."), "media"),
        A("BlenderFoundation.Blender", "Blender", Creation,
            L("3D modeling, animation, rendering, and video editing. Demanding on the graphics card."), "media", "heavy"),
        A("KDE.Kdenlive", "Kdenlive", Creation,
            L("Open-source multitrack video editor with effects and transitions."), "media", "heavy"),
        A("Meltytech.Shotcut", "Shotcut", Creation,
            L("Open-source video editor that's easy to learn."), "media"),
        A("Figma.Figma", "Figma", Creation,
            L("Collaborative interface design (account required)."), "dev"),

        // ------------------------------------------------------------------ Utilitaires
        A("7zip.7zip", "7-Zip", Utilities,
            L("Compress and extract archives (ZIP, 7z, RAR…), open-source and light."), "essentials", "lowend"),
        A("RARLab.WinRAR", "WinRAR", Utilities,
            L("RAR and ZIP archiver. Trial version: a paid license is expected for extended use.")),
        A("voidtools.Everything", "Everything", Utilities,
            L("Instant file search by name across all drives."), "essentials", "lowend"),
        A("Microsoft.PowerToys", "PowerToys", Utilities,
            L("Microsoft utilities: window layouts, bulk renaming, color picker, launcher…"), "essentials", "dev"),
        A("ShareX.ShareX", "ShareX", Utilities,
            L("Screenshots, GIF or video recordings, annotations, and sharing."), "media"),
        A("Greenshot.Greenshot", "Greenshot", Utilities,
            L("Quick screenshots with annotations and area masking."), "office", "lowend"),
        A("CPUID.CPU-Z", "CPU-Z", Utilities,
            L("Detailed information about the processor, motherboard, and memory."))
            with { Match = ["CPUID CPU-Z"] },
        A("TechPowerUp.GPU-Z", "GPU-Z", Utilities,
            L("Graphics card information and sensors."), "gaming")
            with { Match = ["TechPowerUp GPU-Z", "GPU-Z"] },
        A("REALiX.HWiNFO", "HWiNFO", Utilities,
            L("Complete hardware inventory and sensor monitoring (temperatures, voltages, fans).")),
        A("CrystalDewWorld.CrystalDiskInfo", "CrystalDiskInfo", Utilities,
            L("Health (S.M.A.R.T.) and temperature of hard drives and SSDs.")),
        A("CrystalDewWorld.CrystalDiskMark", "CrystalDiskMark", Utilities,
            L("Measures drive read and write speeds.")),
        A("AntibodySoftware.WizTree", "WizTree", Utilities,
            L("See what's taking up disk space in seconds."), "lowend"),
        A("WinDirStat.WinDirStat", "WinDirStat", Utilities,
            L("Visual map of disk usage, open-source.")),
        A("Rufus.Rufus", "Rufus", Utilities,
            L("Create a bootable USB drive to install Windows or Linux.")),
        A("Ventoy.Ventoy", "Ventoy", Utilities,
            L("Multiboot USB drive: just copy ISO files onto it.")),
        A("BleachBit.BleachBit", "BleachBit", Utilities,
            L("Delete temporary files and browsing traces, open-source.")),
        A("Klocman.BulkCrapUninstaller", "Bulk Crap Uninstaller", Utilities,
            L("Uninstall many programs at once and remove their leftovers."))
            with { Match = ["BCUninstaller"] },
        A("File-New-Project.EarTrumpet", "EarTrumpet", Utilities,
            L("Per-app volume from the notification area.")),
        A("Microsoft.Sysinternals.Autoruns", "Autoruns", Utilities,
            L("Everything that starts with Windows, in detail (Microsoft Sysinternals tool)."), "dev"),
        A("Microsoft.Sysinternals.ProcessExplorer", "Process Explorer", Utilities,
            L("Advanced Task Manager (Microsoft Sysinternals tool)."), "dev"),
        A("AutoHotkey.AutoHotkey", "AutoHotkey", Utilities,
            L("Automate tasks and create keyboard shortcuts with scripts."), "dev"),
        A("Flow-Launcher.Flow-Launcher", "Flow Launcher", Utilities,
            L("Keyboard launcher: apps, files, calculations, web search."))
            with { Match = ["Flow Launcher", "Flow-Launcher"] },
        A("QL-Win.QuickLook", "QuickLook", Utilities,
            L("Preview a file by pressing Space in File Explorer, like on macOS.")),
        A("FilesCommunity.Files", "Files", Utilities,
            L("Modern file manager with tabs and panes, an alternative to File Explorer."))
            with { Match = ["Files - Stable", "Files App"] },
        A("LocalSend.LocalSend", "LocalSend", Utilities,
            L("Send files between devices on the same local network, without internet or an account."), "family"),
        A("TeamViewer.TeamViewer", "TeamViewer", Utilities,
            L("Remote support and remote control (free for personal use)."), "family"),
        A("AnyDesk.AnyDesk", "AnyDesk", Utilities,
            L("Light, fast remote control.")),
        A("Google.GoogleDrive", "Google Drive", Utilities,
            L("Access your Google Drive files from File Explorer and sync them.")),
        A("Dropbox.Dropbox", "Dropbox", Utilities,
            L("Sync your Dropbox files with this PC.")),
        A("Nextcloud.NextcloudDesktop", "Nextcloud", Utilities,
            L("Sync client for a Nextcloud server (self-hosted or hosted).")),
        A("qBittorrent.qBittorrent", "qBittorrent", Utilities,
            L("Open-source, ad-free BitTorrent client.")),
        A("Logitech.OptionsPlus", "Logi Options+", Utilities,
            L("Customize the buttons and gestures of Logitech mice and keyboards."))
            with { Match = ["Logi Options+", "Logitech Options+"] },

        // ------------------------------------------------------------------ Sécurité et mots de passe
        A("Bitwarden.Bitwarden", "Bitwarden", Security,
            L("Open-source, encrypted password manager, synced across all your devices."), "essentials", "security", "family"),
        A("KeePassXCTeam.KeePassXC", "KeePassXC", Security,
            L("Offline password manager: an encrypted file that stays with you."), "security"),
        A("DominikReichl.KeePass", "KeePass", Security,
            L("Long-standing offline password manager, extensible with plugins."), "security"),
        A("AgileBits.1Password", "1Password", Security,
            L("Password manager for families and businesses (subscription)."), "security"),
        A("Proton.ProtonPass", "Proton Pass", Security,
            L("Encrypted password and email alias manager, by Proton."), "security"),
        A("Proton.ProtonVPN", "Proton VPN", Security,
            L("VPN with a free plan with no data limit and no logging."), "security"),
        A("WireGuard.WireGuard", "WireGuard", Security,
            L("WireGuard VPN client for your own server or your VPN provider."), "security", "dev"),
        A("Malwarebytes.Malwarebytes", "Malwarebytes", Security,
            L("On-demand anti-malware scan, alongside Microsoft Defender. The Premium trial may register itself as the main antivirus: afterward, check in Windows Security that protection is still on."), "security"),
        A("IDRIX.VeraCrypt", "VeraCrypt", Security,
            L("Encrypt drives, USB drives, or file containers."), "security"),
        A("Cryptomator.Cryptomator", "Cryptomator", Security,
            L("Encrypt your files before uploading them to the cloud (OneDrive, Google Drive, Dropbox…)."), "security"),

        // ------------------------------------------------------------------ Communication
        A("Mozilla.Thunderbird.fr", "Thunderbird", Communication,
            L("Open-source email client: multiple accounts, calendar, and contacts. French-language version."), "essentials", "office", "family")
            with { Match = ["Mozilla Thunderbird"] },
        A("Discord.Discord", "Discord", Communication,
            L("Voice, video, and text chat, widely used by gamers and communities."), "gaming"),
        A("Zoom.Zoom", "Zoom Workplace", Communication,
            L("Video conferencing and online meetings."), "office")
            with { Match = ["Zoom"] },
        A("Microsoft.Teams", "Microsoft Teams", Communication,
            L("Meetings and team chat for work, school, and personal accounts."), "office"),
        A("SlackTechnologies.Slack", "Slack", Communication,
            L("Team messaging organized into channels."), "office"),
        A("OpenWhisperSystems.Signal", "Signal", Communication,
            L("End-to-end encrypted messaging; requires Signal on your phone."), "security", "family"),
        A("Telegram.TelegramDesktop", "Telegram", Communication,
            L("Fast messaging with groups, channels, and cloud sync."))
            with { Match = ["Telegram Desktop"] },
        A("Element.Element", "Element", Communication,
            L("Decentralized, encrypted messaging based on the Matrix protocol."), "security"),

        // ------------------------------------------------------------------ Développement
        A("Microsoft.VisualStudioCode", "Visual Studio Code", Development,
            L("Light, extensible code editor from Microsoft."), "dev")
            with { Match = ["Microsoft Visual Studio Code"] },
        A("Notepad++.Notepad++", "Notepad++", Development,
            L("Fast text and code editor with syntax highlighting and tabs."), "dev", "essentials", "lowend"),
        A("Git.Git", "Git", Development,
            L("Git version control, with Git Bash."), "dev"),
        A("GitHub.GitHubDesktop", "GitHub Desktop", Development,
            L("Simple graphical interface for Git and GitHub."), "dev"),
        A("Python.Python.3.14", "Python 3.14", Development,
            L("The Python language and its “py” launcher."), "dev", "family"),
        A("OpenJS.NodeJS.LTS", "Node.js LTS", Development,
            L("Server-side JavaScript runtime, long-term support (LTS) version."), "dev")
            with { Match = ["Node.js"] },
        A("Microsoft.DotNet.SDK.10", "SDK .NET 10", Development,
            L("Tools for developing .NET 10 apps (C#, F#)."), "dev")
            with { Match = ["Microsoft .NET SDK 10"] },
        A("Microsoft.PowerShell", "PowerShell 7", Development,
            L("Modern, cross-platform version of PowerShell, installed alongside Windows PowerShell 5.1."), "dev")
            with { Match = ["PowerShell 7"] },
        A("Microsoft.WindowsTerminal", L("Windows Terminal"), Development,
            L("Tabbed terminal for PowerShell, Command Prompt, and WSL. Already included in Windows 11."), "dev"),
        A("Microsoft.VisualStudio.2022.Community", "Visual Studio Community 2022", Development,
            L("Microsoft's full development environment, installed without any workloads: add them afterward with Visual Studio Installer. Free for individuals, education, and small teams."), "dev", "heavy"),
        A("JetBrains.Toolbox", "JetBrains Toolbox", Development,
            L("Installs and updates JetBrains IDEs (IntelliJ IDEA, PyCharm, Rider…)."), "dev"),
        A("Docker.DockerDesktop", "Docker Desktop", Development,
            L("Docker containers on Windows; requires WSL 2 and virtualization. Free for personal use and small businesses."),
            "dev", "heavy"),
        A("Oracle.VirtualBox", "VirtualBox", Development,
            L("Free virtual machines to try other operating systems."), "dev", "heavy")
            with { Match = ["Oracle VirtualBox", "Oracle VM VirtualBox"] },
        A("Postman.Postman", "Postman", Development,
            L("Test and document web APIs."), "dev"),
        A("WinSCP.WinSCP", "WinSCP", Development,
            L("SFTP, FTP, and SCP file transfers."), "dev"),
        A("PuTTY.PuTTY", "PuTTY", Development,
            L("Classic SSH and Telnet client."), "dev"),
        A("WinMerge.WinMerge", "WinMerge", Development,
            L("Compare and merge files and folders."), "dev"),
        A("DBBrowserForSQLite.DBBrowserForSQLite", "DB Browser for SQLite", Development,
            L("Browse and edit SQLite databases."), "dev"),
        A("Rustlang.Rustup", "Rust (rustup)", Development,
            L("Rust toolchain installer; requires the Visual Studio C++ build tools."), "dev")
            with { Match = ["Rustup"] },
        A("GoLang.Go", "Go", Development,
            L("The Go language and its tools."), "dev")
            with { Match = ["Go Programming Language"] },

        // ------------------------------------------------------------------ Jeux
        A("Valve.Steam", "Steam", Games,
            L("The largest PC game store, with a library, achievements, and playing with friends."), "gaming"),
        A("EpicGames.EpicGamesLauncher", "Epic Games Launcher", Games,
            L("Epic Games store (Fortnite, free games every week)."), "gaming")
            with { Match = ["Epic Games Launcher"] },
        A("GOG.Galaxy", "GOG Galaxy", Games,
            L("DRM-free game store and unified library for your different platforms."), "gaming")
            with { Match = ["GOG GALAXY", "GOG Galaxy"] },
        A("Ubisoft.Connect", "Ubisoft Connect", Games,
            L("Launcher and store for Ubisoft games."), "gaming"),
        A("ElectronicArts.EADesktop", "EA app", Games,
            L("Launcher and store for Electronic Arts games."), "gaming"),
        A("Playnite.Playnite", "Playnite", Games,
            L("A single library for all your games (Steam, Epic, GOG, emulators…), open-source."), "gaming"),
        A("Nvidia.GeForceNow", "GeForce NOW", Games,
            L("Cloud game streaming: games run on NVIDIA's servers, ideal for a low-end PC with a good connection."),
            "gaming", "lowend")
            with { Match = ["NVIDIA GeForce NOW", "GeForce NOW"] },
        A("Parsec.Parsec", "Parsec", Games,
            L("Play remotely on another PC or share a game with friends, with low latency."), "gaming"),
        A("Mojang.MinecraftLauncher", "Minecraft Launcher", Games,
            L("Official Minecraft: Java Edition launcher (Microsoft account and purchased game required)."), "gaming", "family"),
        A("PrismLauncher.PrismLauncher", "Prism Launcher", Games,
            L("Open-source Minecraft launcher to manage multiple instances and modpacks (Minecraft account required)."), "gaming"),
        A("Guru3D.Afterburner", "MSI Afterburner", Games,
            L("In-game overlay (frames per second, temperatures) and graphics card settings."), "gaming"),
        A("Logitech.GHUB", "Logitech G HUB", Games,
            L("Settings, macros, and lighting for Logitech G gaming peripherals."), "gaming"),

        // ------------------------------------------------------------------ Outils constructeur
        A("Lenovo.SystemUpdate", "Lenovo System Update", Vendor,
            L("Up-to-date drivers and BIOS for ThinkPad, ThinkCentre, and ThinkStation PCs. On IdeaPad, Yoga, and Legion, use Lenovo Vantage (Microsoft Store) instead."), "vendor")
            with { Manufacturer = "Lenovo" },
        A("Dell.CommandUpdate.Universal", "Dell Command | Update", Vendor,
            L("Up-to-date drivers, BIOS, and firmware for Dell PCs (Latitude, OptiPlex, Precision, Vostro, XPS)."), "vendor")
            with { Manufacturer = "Dell", Match = ["Dell Command | Update"] },
        A("HP.ImageAssistant", "HP Image Assistant", Vendor,
            L("HP tool for business PCs (EliteBook, ProBook, ZBook): detects and installs drivers, BIOS, and fixes. On consumer PCs, use HP Support Assistant."), "vendor")
            with { Manufacturer = "HP" },
        A("Asus.ArmouryCrate", "Armoury Crate", Vendor,
            L("Control center for ASUS ROG and TUF PCs (performance modes, lighting, updates). Useless and heavy on other models."),
            "vendor", "gaming")
            with { Manufacturer = "ASUS", Match = ["ARMOURY CRATE", "Armoury Crate"] },
        A("MSI.MSICenter", "MSI Center", Vendor,
            L("Control center for MSI PCs and motherboards: performance profiles, monitoring, updates."), "vendor")
            with { Manufacturer = "MSI" },
        A("Microsoft.SurfaceApp", "Surface", Vendor,
            L("App for Surface devices: battery, pen, warranty, and settings."), "vendor")
            with { Manufacturer = "Microsoft" },

        // ------------------------------------------------------------------ Pilotes graphiques
        A("Intel.IntelDriverAndSupportAssistant", "Intel Driver & Support Assistant", Drivers,
            L("Detects and installs the latest Intel drivers (graphics, Wi-Fi, Bluetooth). On a laptop, the manufacturer's drivers are sometimes still preferable."), "vendor")
            with { GpuVendor = HardwareVendor.Intel, Match = ["Intel® Driver & Support Assistant", "Intel(R) Driver & Support Assistant", "Intel Driver"] },
        A("TechPowerUp.NVCleanstall", "NVCleanstall", Drivers,
            L("Downloads the official NVIDIA driver and installs it without unnecessary components. Third-party tool from TechPowerUp: the official NVIDIA app isn't available through winget."), "vendor", "gaming")
            with { GpuVendor = HardwareVendor.Nvidia },

        // ------------------------------------------------------------------ Runtimes
        A("Microsoft.VCRedist.2015+.x64", "Visual C++ 2015-2022 (x64)", Runtimes,
            L("Visual C++ libraries required by many 64-bit programs and games."), "essentials", "gaming")
            with { Match = ["Microsoft Visual C++ 2015-2022 Redistributable (x64)", "Microsoft Visual C++ v14 Redistributable (x64)"] },
        A("Microsoft.VCRedist.2015+.x86", "Visual C++ 2015-2022 (x86)", Runtimes,
            L("The same libraries for 32-bit programs, which are still common."), "essentials", "gaming")
            with { Match = ["Microsoft Visual C++ 2015-2022 Redistributable (x86)", "Microsoft Visual C++ v14 Redistributable (x86)"] },
        A("Microsoft.DotNet.DesktopRuntime.8", ".NET Desktop Runtime 8", Runtimes,
            L("Runtime for .NET 8 desktop apps."), "essentials")
            with { Match = ["Microsoft Windows Desktop Runtime - 8", "Microsoft .NET Windows Desktop Runtime 8"] },
        A("Microsoft.DotNet.DesktopRuntime.10", ".NET Desktop Runtime 10", Runtimes,
            L("Runtime for .NET 10 desktop apps."))
            with { Match = ["Microsoft Windows Desktop Runtime - 10", "Microsoft .NET Windows Desktop Runtime 10"] },
        A("Microsoft.DirectX", L("DirectX (legacy libraries)"), Runtimes,
            L("Legacy DirectX 9 to 11 libraries (d3dx9, XAudio 2.7…) required by many older games."), "gaming")
            with { Match = ["Microsoft DirectX"] },
        A("Microsoft.XNARedist", "XNA Framework 4.0", Runtimes,
            L("Required by some older indie games (Terraria, Stardew Valley before 1.5…)."), "gaming")
            with { Match = ["Microsoft XNA Framework Redistributable"] },
        A("EclipseAdoptium.Temurin.21.JRE", "Java 21 (Temurin)", Runtimes,
            L("Open-source Java runtime, for programs that need it."))
            with { Match = ["Eclipse Temurin JRE"] },
        A("Microsoft.EdgeWebView2Runtime", "WebView2", Runtimes,
            L("Web component used by many modern apps; already included in Windows 11."))
            with { Match = ["Microsoft Edge WebView2"] },
    ];
}
