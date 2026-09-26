using Timonier.Core.Catalog;
using Timonier.Core.Platform;
using Timonier.Core.Search;
using Timonier.UI.Services;
using Windows.Devices.Radios;

namespace Timonier.Modules.Devices;

/// <summary>
/// Périphériques et matériel : radios, inventaire Plug-and-Play (problèmes, activation/désactivation encadrée),
/// batterie, écrans et blocages matériels pour tout le PC. Déclarations uniquement : appelé aussi dans le broker.
/// </summary>
public sealed class DevicesModule : IModule
{
    public const string Category = "devices";
    public const string PageId = "devices";
    public const string Glyph = "";

    public void Register(ModuleRegistry r)
    {
        r.AddCategory(new CategoryInfo(Category, L("Devices"), Glyph,
            L("Radios, device inventory, battery, displays and hardware blocks (USB, camera, microphone…).")));

        r.AddTweaks(DevicesTweaks.All());
        r.AddAction(new DisableDeviceAction());
        r.AddAction(new DisableSensitiveDeviceAction());
        r.AddAction(new EnableDeviceAction());

        r.AddPage(new PageInfo(PageId, L("Devices"), Glyph, NavSection.Control, 20, () => new DevicesPage())
        {
            CategoryId = Category,
            Description = L("Wi-Fi and Bluetooth, devices with errors, battery health, displays, blocking USB drives, the camera and the microphone."),
            Keywords = [L("devices, hardware, device manager, drivers, usb, bluetooth, wi-fi, battery, display, monitor, camera, microphone")],
        });

        // Contrôles de santé : exécutés hors du thread UI, lecture seule, sans élévation.
        r.AddHealthCheck(HealthCheck.Sync("devices.problems", L("Devices"), Glyph, PageId, ProblemsHealth));
        r.AddHealthCheck(HealthCheck.Sync("devices.battery", L("Battery"), "", PageId, BatteryService.Health));

        r.AddQuickAction(new QuickAction("devices.toggle-bluetooth", L("Turn Bluetooth on or off"), "",
            L("Toggles this PC's Bluetooth radio (like the button in the notification center)."), ToggleBluetoothAsync)
        {
            Keywords = [L("bluetooth, turn off bluetooth, turn on bluetooth, radio, wireless, headset, headphones, earbuds")],
            Order = 40,
        });

        RegisterSearchEntries(r);

        Synonyms.AddGroup("peripherique", "materiel", "device", "hardware", "appareil");
        Synonyms.AddGroup("gestionnaire de peripheriques", "device manager", "devmgmt");
        Synonyms.AddGroup("cle usb", "clef usb", "usb stick", "stockage usb", "disque externe");
        Synonyms.AddGroup("webcam", "camera", "cam");
        Synonyms.AddGroup("wifi", "wi fi", "sans fil", "wlan");
        Synonyms.AddGroup("usure batterie", "sante batterie", "battery health", "battery wear", "capacite batterie");
        Synonyms.AddGroup("frequence rafraichissement", "taux de rafraichissement", "refresh rate", "hz");
    }

    private static HealthResult ProblemsHealth()
    {
        var (problems, disabled) = DeviceInventory.Problems();
        if (problems.Count > 0)
        {
            var names = string.Join(", ", problems.Take(3).Select(p => p.Name)) + (problems.Count > 3 ? "…" : "");
            return new HealthResult(HealthStatus.Warning,
                LP(problems.Count, "{0} device isn't working properly.", "{0} devices aren't working properly."),
                names);
        }
        return disabled > 0
            ? new HealthResult(HealthStatus.Info, LP(disabled, "No devices with errors; {0} disabled on purpose.", "No devices with errors; {0} disabled on purpose."))
            : new HealthResult(HealthStatus.Good, L("All devices are working properly."));
    }

    /// <summary>Action rapide (thread UI) : bascule la première radio Bluetooth trouvée.</summary>
    private static async Task ToggleBluetoothAsync()
    {
        var snap = await RadioService.LoadAsync();
        var bt = snap.Radios.FirstOrDefault(x => x.Kind == RadioKind.Bluetooth);
        if (bt is null)
        {
            AppHost.Toasts.Show(snap.Error is null ? L("No Bluetooth adapter detected on this PC.") : L("The radios aren't accessible: opening Settings."),
                ToastKind.Warning);
            if (snap.Error is not null) ProcessRunner.OpenSettingsUri("ms-settings:bluetooth");
            return;
        }
        var (ok, message) = await RadioService.SetAsync(bt, bt.State != RadioState.On);
        AppHost.Toasts.Show(message, ok ? ToastKind.Success : ToastKind.Warning,
            ok ? null : LC("Windows Settings app", "Settings"), ok ? null : () => ProcessRunner.OpenSettingsUri("ms-settings:bluetooth"));
    }

    private static void RegisterSearchEntries(ModuleRegistry r)
    {
        Feature(r, "devices.section.radios", L("Wi-Fi, Bluetooth and cellular"), L("Turn wireless radios on or off"), "", "radios",
            [L("wifi, wi-fi, bluetooth, radio, wireless, airplane mode, turn off wifi")]);
        Feature(r, "devices.section.inventory", L("Device inventory"), L("Hardware list, devices with errors, drivers, enable or disable"),
            Glyph, "inventory", [L("device manager, hardware list, driver, code 43, code 28, unknown device, disable device, enable device, exclamation mark, yellow warning")]);
        Feature(r, "devices.section.battery", L("Battery health"), L("Wear, capacity, charge cycles and detailed report"), "", "battery",
            [L("battery, wear, cycles, capacity, battery life, battery report, powercfg, battery health")]);
        Feature(r, "devices.section.displays", L("Displays and refresh rate"), L("Resolution and refresh rate of each display"), "", "displays",
            [L("screen, resolution, hz, monitor, display, 144 hz, refresh rate")]);
        Feature(r, "devices.section.blocks", L("Block USB drives, the camera or the microphone"), L("Hardware blocks for all accounts on the PC"), "", "blocks",
            [L("block usb, disable usb drive, usb stick, block camera, block webcam, block microphone, usb read-only, cd dvd, device installation")]);
        Feature(r, "devices.section.protected", L("Protected components"), L("What Timonier refuses to disable, and why"), "", "protected",
            [L("protected, system components, transparency, tpm, system disk")]);

        WindowsSetting(r, "devices.win.bluetooth", L("Bluetooth & devices (Windows Settings)"), L("Pair headphones, a mouse, a phone…"),
            "ms-settings:bluetooth", [L("pair, pairing, add device, bluetooth headphones, connect bluetooth")]);
        WindowsSetting(r, "devices.win.printers", L("Printers & scanners (Windows Settings)"), L("Add or manage a printer"),
            "ms-settings:printers", [L("printer, scanner, printing, print")]);
        WindowsSetting(r, "devices.win.display", L("Display (Windows Settings)"), L("Resolution, scale, multiple displays"),
            "ms-settings:display", [L("display, resolution, scale, scaling, zoom, dual monitor, multiple displays, screen")]);
        WindowsSetting(r, "devices.win.battery", L("Power & battery (Windows Settings)"), L("Battery saver, usage per app"),
            "ms-settings:batterysaver", [L("battery saver, battery life, power saving")]);
        WindowsSetting(r, "devices.win.usb", L("USB (Windows Settings)"), L("USB problem notifications, USB power saving"),
            "ms-settings:usb", [L("usb, usb notification")]);
        WindowsSetting(r, "devices.win.mouse", L("Mouse (Windows Settings)"), L("Pointer speed, buttons, scrolling"),
            "ms-settings:mousetouchpad", [L("mouse, pointer, mouse speed, cursor")]);
        WindowsSetting(r, "devices.win.touchpad", L("Touchpad (Windows Settings)"), L("Gestures, sensitivity, scrolling"),
            "ms-settings:devices-touchpad", [L("touchpad, trackpad, gestures")]);
        WindowsSetting(r, "devices.win.airplane", L("Airplane mode (Windows Settings)"), L("Turn off all wireless communication"),
            "ms-settings:network-airplanemode", [L("airplane mode, flight mode, airplane")]);
    }

    private static void Feature(ModuleRegistry r, string id, string title, string subtitle, string glyph, string section, string[] keywords) =>
        r.AddSearchEntry(new SearchEntry
        {
            Id = id, Title = title, Subtitle = subtitle, Glyph = glyph, Keywords = keywords,
            PageId = PageId, PageParameter = "section:" + section,
        });

    private static void WindowsSetting(ModuleRegistry r, string id, string title, string subtitle, string uri, string[] keywords) =>
        r.AddSearchEntry(new SearchEntry
        {
            Id = id, Title = title, Subtitle = subtitle, Glyph = "", Keywords = keywords,
            Kind = SearchEntryKind.WindowsSetting,
            Execute = () => ProcessRunner.OpenSettingsUri(uri),
        });
}
