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
                ? (true, on ? L("{0} turned on.", KindLabel(radio.Kind)) : L("{0} turned off.", KindLabel(radio.Kind)))
                : (false, result switch
                {
                    RadioAccessStatus.DeniedByUser => L("Windows doesn't allow apps to control radios (“Radios” setting on the Privacy page)."),
                    RadioAccessStatus.DeniedBySystem => L("Windows refused this change (airplane mode managed by the system, or a policy)."),
                    _ => L("Windows couldn't change the state of this radio."),
                });
        }
        catch (Exception ex)
        {
            Log.Warn("Devices", "changement d'état radio : " + ex.Message);
            return (false, L("Windows couldn't change the state of this radio."));
        }
    }

    public static string KindLabel(RadioKind kind) => kind switch
    {
        RadioKind.WiFi => "Wi-Fi",
        RadioKind.Bluetooth => "Bluetooth",
        RadioKind.MobileBroadband => L("Cellular"),
        RadioKind.FM => L("FM radio"),
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
