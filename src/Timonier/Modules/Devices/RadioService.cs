using Timonier.Core.Platform;
using Windows.Devices.Radios;

namespace Timonier.Modules.Devices;

/// <summary>
/// Radios du PC (Wi-Fi, Bluetooth, haut débit mobile) via l'API WinRT Windows.Devices.Radios : même effet que les
/// interrupteurs du centre de notifications. Réglage de l'utilisateur, sans élévation, non persistant au sens des stratégies
/// (le mode Avion ou l'utilisateur peuvent le rechanger).
/// </summary>
public static class RadioService
{
    public sealed record Snapshot(RadioAccessStatus Access, IReadOnlyList<Radio> Radios, string? Error);

    public static async Task<Snapshot> LoadAsync()
    {
        try
        {
            var access = await Radio.RequestAccessAsync();
            var radios = await Radio.GetRadiosAsync();
            var list = radios.Where(r => r.Kind is RadioKind.WiFi or RadioKind.Bluetooth or RadioKind.MobileBroadband)
                             .OrderBy(r => KindOrder(r.Kind)).ToList();
            return new Snapshot(access, list, null);
        }
        catch (Exception ex)
        {
            Log.Warn("Devices", "radios indisponibles : " + ex.Message);
            return new Snapshot(RadioAccessStatus.Unspecified, [], ex.Message);
        }
    }

    public static async Task<(bool Ok, string Message)> SetAsync(Radio radio, bool on)
    {
        try
        {
            var result = await radio.SetStateAsync(on ? RadioState.On : RadioState.Off);
            return result == RadioAccessStatus.Allowed
                ? (true, on ? L("{0} activé.", KindLabel(radio.Kind)) : L("{0} désactivé.", KindLabel(radio.Kind)))
                : (false, result switch
                {
                    RadioAccessStatus.DeniedByUser => L("Windows refuse que les applications contrôlent les radios (réglage « Contrôle des radios » de la page Confidentialité)."),
                    RadioAccessStatus.DeniedBySystem => L("Windows refuse ce changement (mode Avion géré par le système ou stratégie)."),
                    _ => L("Windows n'a pas pu changer l'état de cette radio."),
                });
        }
        catch (Exception ex)
        {
            Log.Warn("Devices", "changement d'état radio : " + ex.Message);
            return (false, L("Windows n'a pas pu changer l'état de cette radio."));
        }
    }

    public static string KindLabel(RadioKind kind) => kind switch
    {
        RadioKind.WiFi => "Wi-Fi",
        RadioKind.Bluetooth => "Bluetooth",
        RadioKind.MobileBroadband => L("Réseau mobile"),
        RadioKind.FM => L("Radio FM"),
        _ => L("Radio"),
    };

    public static string KindGlyph(RadioKind kind) => kind switch
    {
        RadioKind.WiFi => "",
        RadioKind.Bluetooth => "",
        RadioKind.MobileBroadband => "",
        _ => "",
    };

    public static string SettingsUri(RadioKind kind) => kind switch
    {
        RadioKind.WiFi => "ms-settings:network-wifi",
        RadioKind.Bluetooth => "ms-settings:bluetooth",
        RadioKind.MobileBroadband => "ms-settings:network-cellular",
        _ => "ms-settings:network-airplanemode",
    };

    private static int KindOrder(RadioKind kind) => kind switch
    {
        RadioKind.WiFi => 0,
        RadioKind.Bluetooth => 1,
        RadioKind.MobileBroadband => 2,
        _ => 3,
    };
}
