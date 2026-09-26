using System.Runtime.InteropServices;

namespace Timonier.Modules.Devices;

/// <summary>Écran actif sur le bureau, avec son mode courant et les meilleurs modes disponibles.</summary>
public sealed record DisplayInfo(
    string DeviceName, string MonitorName, string AdapterName, bool Primary,
    int Width, int Height, int RefreshHz, int BitsPerPixel, int X, int Y, int Orientation,
    int MaxWidth, int MaxHeight, int MaxRefreshAtCurrent)
{
    public string OrientationLabel => Orientation switch
    {
        1 => LC("orientation", "Portrait"),
        2 => LC("orientation", "Landscape (flipped)"),
        3 => LC("orientation", "Portrait (flipped)"),
        _ => LC("orientation", "Landscape"),
    };
}

/// <summary>Énumération des écrans via EnumDisplayDevices / EnumDisplaySettings (lecture seule, sans élévation).</summary>
public static class DisplayService
{
    private const int EnumCurrentSettings = -1;
    private const int AttachedToDesktop = 0x1;
    private const int PrimaryDevice = 0x4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

#pragma warning disable SYSLIB1054 // Structures non blittables (chaînes de taille fixe) : DllImport classique.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? device, int index, ref DisplayDevice info, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string device, int mode, ref DevMode devMode);
#pragma warning restore SYSLIB1054

    public static List<DisplayInfo> Load()
    {
        var list = new List<DisplayInfo>();
        var friendly = FriendlyNames();
        for (var i = 0; i < 16; i++)
        {
            var adapter = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, i, ref adapter, 0)) break;
            if ((adapter.StateFlags & AttachedToDesktop) == 0) continue;

            var current = NewDevMode();
            if (!EnumDisplaySettings(adapter.DeviceName, EnumCurrentSettings, ref current)) continue;

            var monitor = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };
            var hasMonitor = EnumDisplayDevices(adapter.DeviceName, 0, ref monitor, 0);
            var primary = (adapter.StateFlags & PrimaryDevice) != 0;
            var model = hasMonitor ? ModelCode(monitor.DeviceID) : null;
            var monitorName = model is not null && friendly.TryGetValue(model, out var f) ? f
                : hasMonitor && !string.IsNullOrWhiteSpace(monitor.DeviceString) && !IsGeneric(monitor.DeviceString) ? monitor.DeviceString
                : primary ? L("Main display") : L("Secondary display");

            // Meilleurs modes proposés par le pilote (utile pour repérer un écran 120/144 Hz réglé à 60 Hz).
            int maxW = current.dmPelsWidth, maxH = current.dmPelsHeight, maxHz = current.dmDisplayFrequency;
            for (var m = 0; m < 2000; m++)
            {
                var mode = NewDevMode();
                if (!EnumDisplaySettings(adapter.DeviceName, m, ref mode)) break;
                if ((long)mode.dmPelsWidth * mode.dmPelsHeight > (long)maxW * maxH) { maxW = mode.dmPelsWidth; maxH = mode.dmPelsHeight; }
                if (mode.dmPelsWidth == current.dmPelsWidth && mode.dmPelsHeight == current.dmPelsHeight && mode.dmDisplayFrequency > maxHz)
                    maxHz = mode.dmDisplayFrequency;
            }

            list.Add(new DisplayInfo(adapter.DeviceName, monitorName, adapter.DeviceString, primary,
                current.dmPelsWidth, current.dmPelsHeight, current.dmDisplayFrequency, current.dmBitsPerPel,
                current.dmPositionX, current.dmPositionY, current.dmDisplayOrientation, maxW, maxH, maxHz));
        }
        return [.. list.OrderByDescending(d => d.Primary).ThenBy(d => d.X)];
    }

    /// <summary>« MONITOR\JDZ102D\{…}\0001 » ou « DISPLAY\JDZ102D\4&amp;…_0 » → « JDZ102D ».</summary>
    private static string? ModelCode(string? id) => id?.Split('\\') is { Length: >= 2 } parts && parts[1].Length > 0 ? parts[1] : null;

    private static bool IsGeneric(string name) =>
        name.Contains("Generic", StringComparison.OrdinalIgnoreCase) || name.Contains("générique", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("PnP", StringComparison.OrdinalIgnoreCase);

    /// <summary>Noms commerciaux des écrans (EDID, root\wmi WmiMonitorID). Souvent vide pour les dalles intégrées.</summary>
    private static Dictionary<string, string> FriendlyNames()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var row in Timonier.Core.Platform.WmiQuery.Query("SELECT InstanceName, UserFriendlyName FROM WmiMonitorID", @"root\wmi", 10))
            {
                if (ModelCode(row.GetValueOrDefault("InstanceName") as string) is not { } model) continue;
                if (row.GetValueOrDefault("UserFriendlyName") is not ushort[] chars) continue;
                var name = new string([.. chars.TakeWhile(ch => ch != 0).Select(ch => (char)ch)]).Trim();
                if (name.Length > 0) map[model] = name;
            }
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            Timonier.Core.Platform.Log.Warn("Devices", "noms des écrans : " + ex.Message);
        }
        return map;
    }

    private static DevMode NewDevMode() => new() { dmSize = (short)Marshal.SizeOf<DevMode>(), dmDeviceName = "", dmFormName = "" };
}
