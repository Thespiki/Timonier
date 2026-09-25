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
        r.AddCategory(new CategoryInfo(Category, "Périphériques", Glyph,
            "Radios, inventaire des périphériques, batterie, écrans et blocages matériels (USB, caméra, micro…)."));

        r.AddTweaks(DevicesTweaks.All());
        r.AddAction(new DisableDeviceAction());
        r.AddAction(new DisableSensitiveDeviceAction());
        r.AddAction(new EnableDeviceAction());

        r.AddPage(new PageInfo(PageId, "Périphériques", Glyph, NavSection.Control, 20, () => new DevicesPage())
        {
            CategoryId = Category,
            Description = "Wi-Fi et Bluetooth, périphériques en erreur, santé de la batterie, écrans, blocage des clés USB, de la caméra et du micro.",
            Keywords = ["périphériques", "matériel", "hardware", "devices", "gestionnaire de périphériques", "pilotes", "usb", "bluetooth",
                        "wi-fi", "batterie", "écran", "caméra", "micro"],
        });

        // Contrôles de santé : exécutés hors du thread UI, lecture seule, sans élévation.
        r.AddHealthCheck(HealthCheck.Sync("devices.problems", "Périphériques", Glyph, PageId, ProblemsHealth));
        r.AddHealthCheck(HealthCheck.Sync("devices.battery", "Batterie", "", PageId, BatteryService.Health));

        r.AddQuickAction(new QuickAction("devices.toggle-bluetooth", "Activer ou couper le Bluetooth", "",
            "Bascule la radio Bluetooth de ce PC (comme le bouton du centre de notifications).", ToggleBluetoothAsync)
        {
            Keywords = ["bluetooth", "couper bluetooth", "activer bluetooth", "radio", "sans fil", "casque", "écouteurs"],
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
                problems.Count == 1 ? "1 périphérique ne fonctionne pas correctement." : $"{problems.Count} périphériques ne fonctionnent pas correctement.",
                names);
        }
        return disabled > 0
            ? new HealthResult(HealthStatus.Info, $"Aucun périphérique en erreur ; {disabled} désactivé(s) volontairement.")
            : new HealthResult(HealthStatus.Good, "Tous les périphériques fonctionnent correctement.");
    }

    /// <summary>Action rapide (thread UI) : bascule la première radio Bluetooth trouvée.</summary>
    private static async Task ToggleBluetoothAsync()
    {
        var snap = await RadioService.LoadAsync();
        var bt = snap.Radios.FirstOrDefault(x => x.Kind == RadioKind.Bluetooth);
        if (bt is null)
        {
            AppHost.Toasts.Show(snap.Error is null ? "Aucun adaptateur Bluetooth détecté sur ce PC." : "Les radios ne sont pas accessibles : ouverture des Paramètres.",
                ToastKind.Warning);
            if (snap.Error is not null) ProcessRunner.OpenSettingsUri("ms-settings:bluetooth");
            return;
        }
        var (ok, message) = await RadioService.SetAsync(bt, bt.State != RadioState.On);
        AppHost.Toasts.Show(message, ok ? ToastKind.Success : ToastKind.Warning,
            ok ? null : "Paramètres", ok ? null : () => ProcessRunner.OpenSettingsUri("ms-settings:bluetooth"));
    }

    private static void RegisterSearchEntries(ModuleRegistry r)
    {
        Feature(r, "devices.section.radios", "Wi-Fi, Bluetooth et réseau mobile", "Allumer ou couper les radios sans fil", "", "radios",
            ["wifi", "bluetooth", "radio", "sans fil", "mode avion", "couper wifi"]);
        Feature(r, "devices.section.inventory", "Inventaire des périphériques", "Liste du matériel, périphériques en erreur, pilotes, activer ou désactiver",
            Glyph, "inventory", ["gestionnaire de peripheriques", "liste materiel", "pilote", "driver", "code 43", "code 28", "periphérique inconnu",
                                  "desactiver peripherique", "activer peripherique", "point d'exclamation"]);
        Feature(r, "devices.section.battery", "Santé de la batterie", "Usure, capacité, cycles de charge et rapport détaillé", "", "battery",
            ["batterie", "usure", "cycles", "capacite", "autonomie", "battery report", "powercfg"]);
        Feature(r, "devices.section.displays", "Écrans et fréquence de rafraîchissement", "Résolution et fréquence de chaque écran", "", "displays",
            ["ecran", "resolution", "hz", "moniteur", "affichage", "144 hz", "refresh rate"]);
        Feature(r, "devices.section.blocks", "Bloquer les clés USB, la caméra ou le micro", "Blocages matériels pour tous les comptes du PC", "", "blocks",
            ["bloquer usb", "interdire cle usb", "bloquer camera", "bloquer micro", "lecture seule usb", "cd dvd", "installation peripherique"]);
        Feature(r, "devices.section.protected", "Composants protégés", "Ce que Timonier refuse de désactiver, et pourquoi", "", "protected",
            ["protege", "composants systeme", "transparence", "tpm", "disque systeme"]);

        WindowsSetting(r, "devices.win.bluetooth", "Bluetooth et appareils (Paramètres Windows)", "Associer un casque, une souris, un téléphone…",
            "ms-settings:bluetooth", ["associer", "appairer", "pairing", "ajouter appareil", "casque bluetooth"]);
        WindowsSetting(r, "devices.win.printers", "Imprimantes et scanners (Paramètres Windows)", "Ajouter ou gérer une imprimante",
            "ms-settings:printers", ["imprimante", "scanner", "printer", "impression"]);
        WindowsSetting(r, "devices.win.display", "Affichage (Paramètres Windows)", "Résolution, échelle, écrans multiples",
            "ms-settings:display", ["affichage", "resolution", "echelle", "zoom", "double ecran"]);
        WindowsSetting(r, "devices.win.battery", "Alimentation et batterie (Paramètres Windows)", "Économiseur de batterie, utilisation par application",
            "ms-settings:batterysaver", ["economiseur batterie", "battery saver", "autonomie"]);
        WindowsSetting(r, "devices.win.usb", "USB (Paramètres Windows)", "Notifications de problèmes USB, économie d'énergie USB",
            "ms-settings:usb", ["usb", "notification usb"]);
        WindowsSetting(r, "devices.win.mouse", "Souris (Paramètres Windows)", "Vitesse du pointeur, boutons, défilement",
            "ms-settings:mousetouchpad", ["souris", "pointeur", "vitesse souris", "mouse"]);
        WindowsSetting(r, "devices.win.touchpad", "Pavé tactile (Paramètres Windows)", "Gestes, sensibilité, défilement",
            "ms-settings:devices-touchpad", ["pave tactile", "touchpad", "trackpad", "gestes"]);
        WindowsSetting(r, "devices.win.airplane", "Mode Avion (Paramètres Windows)", "Couper toutes les communications sans fil",
            "ms-settings:network-airplanemode", ["mode avion", "airplane", "avion"]);
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
