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
        r.AddCategory(new CategoryInfo(Category, L("Périphériques"), Glyph,
            L("Radios, inventaire des périphériques, batterie, écrans et blocages matériels (USB, caméra, micro…).")));

        r.AddTweaks(DevicesTweaks.All());
        r.AddAction(new DisableDeviceAction());
        r.AddAction(new DisableSensitiveDeviceAction());
        r.AddAction(new EnableDeviceAction());

        r.AddPage(new PageInfo(PageId, L("Périphériques"), Glyph, NavSection.Control, 20, () => new DevicesPage())
        {
            CategoryId = Category,
            Description = L("Wi-Fi et Bluetooth, périphériques en erreur, santé de la batterie, écrans, blocage des clés USB, de la caméra et du micro."),
            Keywords = [L("périphériques, matériel, hardware, devices, gestionnaire de périphériques, pilotes, usb, bluetooth, wi-fi, batterie, écran, caméra, micro")],
        });

        // Contrôles de santé : exécutés hors du thread UI, lecture seule, sans élévation.
        r.AddHealthCheck(HealthCheck.Sync("devices.problems", L("Périphériques"), Glyph, PageId, ProblemsHealth));
        r.AddHealthCheck(HealthCheck.Sync("devices.battery", L("Batterie"), "", PageId, BatteryService.Health));

        r.AddQuickAction(new QuickAction("devices.toggle-bluetooth", L("Activer ou couper le Bluetooth"), "",
            L("Bascule la radio Bluetooth de ce PC (comme le bouton du centre de notifications)."), ToggleBluetoothAsync)
        {
            Keywords = [L("bluetooth, couper bluetooth, activer bluetooth, radio, sans fil, casque, écouteurs")],
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
                LP(problems.Count, "{0} périphérique ne fonctionne pas correctement.", "{0} périphériques ne fonctionnent pas correctement."),
                names);
        }
        return disabled > 0
            ? new HealthResult(HealthStatus.Info, LP(disabled, "Aucun périphérique en erreur ; {0} désactivé volontairement.", "Aucun périphérique en erreur ; {0} désactivés volontairement."))
            : new HealthResult(HealthStatus.Good, L("Tous les périphériques fonctionnent correctement."));
    }

    /// <summary>Action rapide (thread UI) : bascule la première radio Bluetooth trouvée.</summary>
    private static async Task ToggleBluetoothAsync()
    {
        var snap = await RadioService.LoadAsync();
        var bt = snap.Radios.FirstOrDefault(x => x.Kind == RadioKind.Bluetooth);
        if (bt is null)
        {
            AppHost.Toasts.Show(snap.Error is null ? L("Aucun adaptateur Bluetooth détecté sur ce PC.") : L("Les radios ne sont pas accessibles : ouverture des Paramètres."),
                ToastKind.Warning);
            if (snap.Error is not null) ProcessRunner.OpenSettingsUri("ms-settings:bluetooth");
            return;
        }
        var (ok, message) = await RadioService.SetAsync(bt, bt.State != RadioState.On);
        AppHost.Toasts.Show(message, ok ? ToastKind.Success : ToastKind.Warning,
            ok ? null : L("Paramètres"), ok ? null : () => ProcessRunner.OpenSettingsUri("ms-settings:bluetooth"));
    }

    private static void RegisterSearchEntries(ModuleRegistry r)
    {
        Feature(r, "devices.section.radios", L("Wi-Fi, Bluetooth et réseau mobile"), L("Allumer ou couper les radios sans fil"), "", "radios",
            [L("wifi, bluetooth, radio, sans fil, mode avion, couper wifi")]);
        Feature(r, "devices.section.inventory", L("Inventaire des périphériques"), L("Liste du matériel, périphériques en erreur, pilotes, activer ou désactiver"),
            Glyph, "inventory", [L("gestionnaire de peripheriques, liste materiel, pilote, driver, code 43, code 28, periphérique inconnu, desactiver peripherique, activer peripherique, point d'exclamation")]);
        Feature(r, "devices.section.battery", L("Santé de la batterie"), L("Usure, capacité, cycles de charge et rapport détaillé"), "", "battery",
            [L("batterie, usure, cycles, capacite, autonomie, battery report, powercfg")]);
        Feature(r, "devices.section.displays", L("Écrans et fréquence de rafraîchissement"), L("Résolution et fréquence de chaque écran"), "", "displays",
            [L("ecran, resolution, hz, moniteur, affichage, 144 hz, refresh rate")]);
        Feature(r, "devices.section.blocks", L("Bloquer les clés USB, la caméra ou le micro"), L("Blocages matériels pour tous les comptes du PC"), "", "blocks",
            [L("bloquer usb, interdire cle usb, bloquer camera, bloquer micro, lecture seule usb, cd dvd, installation peripherique")]);
        Feature(r, "devices.section.protected", L("Composants protégés"), L("Ce que Timonier refuse de désactiver, et pourquoi"), "", "protected",
            [L("protege, composants systeme, transparence, tpm, disque systeme")]);

        WindowsSetting(r, "devices.win.bluetooth", L("Bluetooth et appareils (Paramètres Windows)"), L("Associer un casque, une souris, un téléphone…"),
            "ms-settings:bluetooth", [L("associer, appairer, pairing, ajouter appareil, casque bluetooth")]);
        WindowsSetting(r, "devices.win.printers", L("Imprimantes et scanners (Paramètres Windows)"), L("Ajouter ou gérer une imprimante"),
            "ms-settings:printers", [L("imprimante, scanner, printer, impression")]);
        WindowsSetting(r, "devices.win.display", L("Affichage (Paramètres Windows)"), L("Résolution, échelle, écrans multiples"),
            "ms-settings:display", [L("affichage, resolution, echelle, zoom, double ecran")]);
        WindowsSetting(r, "devices.win.battery", L("Alimentation et batterie (Paramètres Windows)"), L("Économiseur de batterie, utilisation par application"),
            "ms-settings:batterysaver", [L("economiseur batterie, battery saver, autonomie")]);
        WindowsSetting(r, "devices.win.usb", L("USB (Paramètres Windows)"), L("Notifications de problèmes USB, économie d'énergie USB"),
            "ms-settings:usb", [L("usb, notification usb")]);
        WindowsSetting(r, "devices.win.mouse", L("Souris (Paramètres Windows)"), L("Vitesse du pointeur, boutons, défilement"),
            "ms-settings:mousetouchpad", [L("souris, pointeur, vitesse souris, mouse")]);
        WindowsSetting(r, "devices.win.touchpad", L("Pavé tactile (Paramètres Windows)"), L("Gestes, sensibilité, défilement"),
            "ms-settings:devices-touchpad", [L("pave tactile, touchpad, trackpad, gestes")]);
        WindowsSetting(r, "devices.win.airplane", L("Mode Avion (Paramètres Windows)"), L("Couper toutes les communications sans fil"),
            "ms-settings:network-airplanemode", [L("mode avion, airplane, avion")]);
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
