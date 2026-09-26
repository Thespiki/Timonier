using System.Text.RegularExpressions;

namespace Timonier.Modules.Apps;

public enum BloatRisk
{
    /// <summary>Sans conséquence pour Windows : application promotionnelle ou facultative.</summary>
    Safe,
    /// <summary>Supprimable, mais une fonction utile disparaît (lecteur par défaut, jeux Xbox, téléphone…).</summary>
    Moderate,
}

/// <summary>Application préinstallée connue, supprimable pour l'utilisateur courant.</summary>
/// <param name="Pattern">Nom de paquet exact, ou préfixe s'il se termine par « . » ou « * ».</param>
public sealed record BloatEntry(string Pattern, string Title, string Description, BloatRisk Risk, bool Recommended)
{
    public string? Warning { get; init; }

    public bool Matches(string packageName)
    {
        if (Pattern.EndsWith('*')) return packageName.StartsWith(Pattern[..^1], StringComparison.OrdinalIgnoreCase);
        if (Pattern.EndsWith('.')) return packageName.StartsWith(Pattern, StringComparison.OrdinalIgnoreCase);
        return string.Equals(packageName, Pattern, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Table des applications préinstallées supprimables (avec explication et niveau de risque) et liste des composants
/// PROTÉGÉS que Timonier refuse de supprimer (Store, installateur d'applications, bibliothèques, interface de Windows…).
/// </summary>
public static partial class BloatCatalog
{
    private static readonly string Xbox = L("Xbox and Game Pass games use it (sign-in, invites, captures): keep it if you play games on this PC.");

    public static IReadOnlyList<BloatEntry> Entries { get; } =
    [
        new("king.com.", L("King games (Candy Crush…)"), L("Free games with in-app purchases, installed automatically by Windows."), BloatRisk.Safe, true),
        new("Microsoft.BingNews", L("News"), L("Microsoft Start news feed."), BloatRisk.Safe, true),
        new("Microsoft.BingWeather", L("Weather"), L("Weather forecasts from Microsoft (MSN)."), BloatRisk.Safe, false),
        new("Microsoft.BingSearch", L("Bing Search"), L("Bing web search app."), BloatRisk.Safe, true),
        new("Microsoft.Getstarted", L("Tips"), L("Tips for getting started with Windows."), BloatRisk.Safe, true),
        new("Microsoft.GetHelp", L("Get Help"), L("Microsoft online support."), BloatRisk.Moderate, false)
        {
            Warning = L("On Windows 11, troubleshooters run through this app."),
        },
        new("Microsoft.MicrosoftSolitaireCollection", "Microsoft Solitaire Collection", L("Card games with ads (subscription to remove them)."), BloatRisk.Safe, true),
        new("Microsoft.People", L("Contacts"), L("Legacy People (Contacts) app from Windows 10."), BloatRisk.Safe, true),
        new("Microsoft.WindowsFeedbackHub", L("Feedback Hub"), L("Send feedback and report bugs to Microsoft."), BloatRisk.Safe, true),
        new("Microsoft.WindowsMaps", L("Maps"), L("Bing maps and directions (discontinued by Microsoft)."), BloatRisk.Safe, true),
        new("Microsoft.ZuneVideo", L("Movies & TV"), L("Legacy video player and movie store."), BloatRisk.Safe, true),
        new("Microsoft.MicrosoftOfficeHub", "Microsoft 365 (Office)", L("Promotional portal to Office online and the Microsoft 365 subscription."), BloatRisk.Safe, true),
        new("Microsoft.SkypeApp", "Skype", L("Service discontinued by Microsoft in May 2025."), BloatRisk.Safe, true),
        new("Clipchamp.Clipchamp", "Clipchamp", L("Microsoft online video editor (account required)."), BloatRisk.Safe, true),
        new("Microsoft.Todos", "Microsoft To Do", L("To-do lists synced with a Microsoft account."), BloatRisk.Safe, false),
        new("Microsoft.PowerAutomateDesktop", "Power Automate", L("Task automation for advanced users."), BloatRisk.Safe, true),
        new("MicrosoftTeams", L("Teams (Chat)"), L("Legacy personal Teams chat app from Windows 11 (version 21H2/22H2)."), BloatRisk.Safe, true),
        new("MSTeams", "Microsoft Teams", L("New Teams app. Keep it if you use it for work or school."), BloatRisk.Safe, false),
        new("Microsoft.Copilot", "Copilot", L("Microsoft AI assistant (app)."), BloatRisk.Safe, false),
        new("Microsoft.OutlookForWindows", L("Outlook (new)"), L("New Outlook email app, which replaces Mail and Calendar."), BloatRisk.Safe, false),
        new("microsoft.windowscommunicationsapps", L("Mail and Calendar"), L("Legacy apps retired by Microsoft at the end of 2024."), BloatRisk.Safe, true),
        new("Microsoft.549981C3F5F10", "Cortana", L("Voice assistant discontinued by Microsoft."), BloatRisk.Safe, true),
        new("Disney.", "Disney+", L("Promotional shortcut to the streaming service."), BloatRisk.Safe, true),
        new("SpotifyAB.SpotifyMusic", "Spotify", L("Music streaming. Keep it if you use it."), BloatRisk.Safe, false),
        new("BytedancePte.Ltd.TikTok", "TikTok", L("Preinstalled promotional app."), BloatRisk.Safe, true),
        new("Facebook.", "Facebook, Instagram, Messenger", L("Preinstalled promotional apps."), BloatRisk.Safe, true),
        new("7EE7776C.LinkedInforWindows", "LinkedIn", L("Preinstalled promotional app."), BloatRisk.Safe, true),
        new("AmazonVideo.PrimeVideo", "Prime Video", L("Promotional shortcut to the streaming service."), BloatRisk.Safe, true),
        new("Microsoft.MixedReality.Portal", L("Mixed Reality Portal"), L("Windows Mixed Reality headsets (discontinued platform)."), BloatRisk.Safe, true),
        new("Microsoft.Microsoft3DViewer", L("3D Viewer"), L("View 3D models (removed from newer versions)."), BloatRisk.Safe, true),
        new("Microsoft.MSPaint", "Paint 3D", L("Discontinued 3D editor (classic Paint isn't affected)."), BloatRisk.Safe, true),
        new("Microsoft.Print3D", "Print 3D", L("3D print preparation (discontinued)."), BloatRisk.Safe, true),
        new("Microsoft.Wallet", L("Wallet"), L("Legacy Microsoft wallet, unused."), BloatRisk.Safe, true),
        new("Microsoft.Messaging", LC("app name", "Messaging"), L("Legacy SMS app from Windows 10."), BloatRisk.Safe, true),
        new("Microsoft.OneConnect", L("Mobile Plans"), L("Buy cellular data plans (discontinued)."), BloatRisk.Safe, true),
        new("Microsoft.Office.OneNote", L("OneNote for Windows 10"), L("Legacy version of OneNote, replaced by OneNote from Microsoft 365."), BloatRisk.Safe, false),
        new("Microsoft.Windows.DevHome", "Dev Home", L("Developer dashboard, discontinued by Microsoft."), BloatRisk.Safe, true),
        new("Microsoft.MicrosoftJournal", "Journal", L("Handwritten note-taking with a pen."), BloatRisk.Safe, false),
        new("MicrosoftCorporationII.MicrosoftFamily", "Microsoft Family Safety", L("Online parental controls. Keep it if you use it."), BloatRisk.Safe, false),
        new("Microsoft.MicrosoftStickyNotes", L("Sticky Notes"), L("Sticky notes synced with a Microsoft account."), BloatRisk.Safe, false),
        new("Microsoft.WindowsAlarms", L("Clock"), L("Alarms, timer, stopwatch and focus sessions."), BloatRisk.Safe, false),
        new("Microsoft.WindowsSoundRecorder", L("Sound Recorder"), L("Quick microphone recording."), BloatRisk.Safe, false),
        new("Microsoft.ZuneMusic", L("Media Player"), L("Default audio and video player in Windows 11."), BloatRisk.Moderate, false)
        {
            Warning = L("It's the default player: install another one (VLC…) before removing it."),
        },
        new("Microsoft.XboxApp", L("Xbox Console Companion"), L("Legacy Xbox app from Windows 10."), BloatRisk.Moderate, false) { Warning = Xbox },
        new("Microsoft.GamingApp", "Xbox", L("Game Pass store and Xbox game library."), BloatRisk.Moderate, false) { Warning = Xbox },
        new("Microsoft.XboxGamingOverlay", "Xbox Game Bar", L("Game Bar (Win+G): captures, performance, chat."), BloatRisk.Moderate, false) { Warning = Xbox },
        new("Microsoft.XboxIdentityProvider", L("Xbox Identity Provider"), L("Sign-in to your Xbox account in games (Minecraft, Game Pass…)."), BloatRisk.Moderate, false) { Warning = Xbox },
        new("Microsoft.XboxSpeechToTextOverlay", L("Xbox speech-to-text"), L("Captions for voice chat in games."), BloatRisk.Moderate, false) { Warning = Xbox },
        new("Microsoft.Xbox.TCUI", L("Xbox in-game UI"), L("Xbox profiles, friends and invites in games."), BloatRisk.Moderate, false) { Warning = Xbox },
        new("Microsoft.YourPhone", L("Phone Link"), L("Connects your Android phone or iPhone to your PC (texts, calls, photos)."), BloatRisk.Moderate, false)
        {
            Warning = L("You'll no longer be able to get texts, calls and phone notifications on this PC."),
        },
        new("MicrosoftWindows.CrossDevice", L("Cross-device experience"), L("Phone Link component (phone in the Start menu)."), BloatRisk.Moderate, false)
        {
            Warning = L("Phone Link and phone integration in the Start menu will stop working."),
        },
        new("MicrosoftWindows.Client.WebExperience", "Widgets", L("Widgets panel (news, weather) on the taskbar."), BloatRisk.Moderate, false)
        {
            Warning = L("The widgets panel goes away, and so does the weather on the taskbar."),
        },
        new("Microsoft.WindowsCamera", L("Camera"), L("Windows Camera app."), BloatRisk.Moderate, false)
        {
            Warning = L("You'll no longer have an app to take photos or test your webcam."),
        },
        new("Microsoft.ScreenSketch", L("Snipping Tool"), L("Screenshots with Win+Shift+S and Print Screen."), BloatRisk.Moderate, false)
        {
            Warning = L("Screenshot shortcuts (Win+Shift+S) will stop working."),
        },
        new("MicrosoftCorporationII.QuickAssist", L("Quick Assist"), L("Remote help between two Windows PCs."), BloatRisk.Moderate, false)
        {
            Warning = L("You'll no longer be able to get remote help with Quick Assist."),
        },
        new("Microsoft.Windows.Photos", L("Photos"), L("Default image and video viewer."), BloatRisk.Moderate, false)
        {
            Warning = L("It's the default viewer: install another one (ImageGlass, IrfanView…) before removing it."),
        },
        new("Microsoft.WindowsCalculator", L("Calculator"), L("Windows Calculator."), BloatRisk.Moderate, false)
        {
            Warning = L("Windows will no longer have a calculator; the Calculator key on your keyboard will do nothing."),
        },
        new("Microsoft.WindowsNotepad", L("Notepad"), L("Windows text editor."), BloatRisk.Moderate, false)
        {
            Warning = L("Text files will no longer have a default editor."),
        },
        new("Microsoft.Paint", "Paint", L("Simple drawing and editing."), BloatRisk.Moderate, false)
        {
            Warning = L("Paint will no longer be available for quick edits."),
        },
        new("Microsoft.WindowsTerminal", L("Windows Terminal"), L("Default terminal in Windows 11."), BloatRisk.Moderate, false)
        {
            Warning = L("Windows will fall back to the old console; some scripts and shortcuts open in Terminal."),
        },
    ];

    /// <summary>
    /// Composants indispensables : jamais supprimés par Timonier (Store, installateur d'applications, bibliothèques,
    /// interface de Windows, sécurité, comptes, extensions multimédias, packs de langue…). Préfixe si terminé par « . » ou « * ».
    /// </summary>
    public static IReadOnlyList<string> Protected { get; } =
    [
        "Microsoft.WindowsStore", "Microsoft.StorePurchaseApp", "Microsoft.DesktopAppInstaller", "Microsoft.Winget.Source",
        "Microsoft.VCLibs*", "Microsoft.UI.Xaml*", "Microsoft.NET.Native*", "Microsoft.WindowsAppRuntime*", "Microsoft.WinAppRuntime*",
        "Microsoft.Services.Store.Engagement", "Microsoft.Windows.ShellExperienceHost", "Microsoft.Windows.StartMenuExperienceHost",
        "Microsoft.SecHealthUI", "Microsoft.AAD.BrokerPlugin", "Microsoft.AccountsControl", "Microsoft.Windows.CloudExperienceHost",
        "Microsoft.LockApp", "Microsoft.Windows.ContentDeliveryManager", "Microsoft.Windows.SecureAssessmentBrowser",
        "Microsoft.Windows.ParentalControls", "Microsoft.Windows.PeopleExperienceHost", "Microsoft.Windows.Search",
        "Microsoft.Windows.AssignedAccessLockApp", "Microsoft.BioEnrollment", "Microsoft.CredDialogHost", "Microsoft.ECApp",
        "Microsoft.Win32WebViewHost", "Microsoft.AsyncTextService", "Microsoft.Windows.Apprep.ChxApp", "Microsoft.Windows.OOBE*",
        "Microsoft.Windows.CapturePicker", "Microsoft.Windows.PinningConfirmationDialog", "Microsoft.Windows.PrintQueueActionCenter",
        "Microsoft.Windows.XGpuEjectDialog", "Microsoft.Windows.NarratorQuickStart", "Microsoft.XboxGameCallableUI",
        "Microsoft.MicrosoftEdge*", "Microsoft.OneDriveSync", "Microsoft.ApplicationCompatibilityEnhancements",
        "Microsoft.LanguageExperiencePack*", "Microsoft.*Extension*", "Microsoft.WebMediaExtensions", "Microsoft.HEVCVideoExtension",
        "Microsoft.MPEG2VideoExtension", "Microsoft.Windows.DevicesFlowHost", "Microsoft.WindowsAppSDK*",
        "MicrosoftWindows.Client.*", "MicrosoftWindows.UndockedDevKit", "MicrosoftWindows.Speech*", "MicrosoftWindows.Voice*",
        "windows.immersivecontrolpanel", "Windows.PrintDialog", "Windows.CBSPreview", "Microsoft.MicrosoftEdgeDevToolsClient",
        "NcsiUwpApp", "Microsoft.Windows.FileExplorer*", "Microsoft.Windows.FilePicker*", "MicrosoftWindows.*",
    ];

    /// <summary>Correspondance dans la table des applications supprimables (null si inconnue).</summary>
    public static BloatEntry? Find(string packageName) => Entries.FirstOrDefault(e => e.Matches(packageName));

    /// <summary>
    /// Vrai si le paquet est un composant protégé. Les entrées explicitement listées comme supprimables
    /// (ex. Widgets, multi-appareils) l'emportent sur les motifs génériques « MicrosoftWindows.* ».
    /// </summary>
    public static bool IsProtected(string packageName)
    {
        if (Find(packageName) is not null) return false;
        return Protected.Any(p => WildcardMatch(packageName, p));
    }

    private static bool WildcardMatch(string value, string pattern)
    {
        if (!pattern.Contains('*')) return string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase);
        var rx = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(value, rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    // ------------------------------------------------------------------ Validation des identifiants de paquets

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.\-]{1,99}_[a-z0-9]{13}$")]
    private static partial Regex FamilyRx();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.\-]{1,99}_[0-9]{1,5}(\.[0-9]{1,5}){3}_[A-Za-z0-9]{0,10}_[A-Za-z0-9.\-~]{0,60}_[a-z0-9]{13}$")]
    private static partial Regex FullNameRx();

    public static bool IsValidFamilyName(string value) => FamilyRx().IsMatch(value);

    public static bool IsValidFullName(string value) => FullNameRx().IsMatch(value);

    /// <summary>Nom du paquet (partie avant le « _ ») d'un nom de famille ou d'un nom complet.</summary>
    public static string NameOf(string familyOrFullName) => familyOrFullName.Split('_')[0];
}
