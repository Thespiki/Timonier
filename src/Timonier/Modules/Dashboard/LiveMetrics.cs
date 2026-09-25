using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Timonier.Modules.Dashboard;

/// <summary>API Win32 utilisées par le tableau de bord (lecture seule, sauf le verrouillage de session demandé par l'utilisateur).</summary>
internal static partial class DashNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetSystemPowerStatus(out SystemPowerStatus status);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool LockWorkStation();
}

/// <summary>Instantané des mesures en direct.</summary>
internal sealed record LiveSample
{
    public double? CpuRatio { get; init; }
    public ulong RamTotal { get; init; }
    public ulong RamUsed { get; init; }
    public string? DriveName { get; init; }
    public long DriveTotal { get; init; }
    public long DriveFree { get; init; }
    public bool HasBattery { get; init; }
    public int? BatteryPercent { get; init; }
    public bool? OnAc { get; init; }
    public bool Charging { get; init; }
    public TimeSpan? BatteryRemaining { get; init; }
    public TimeSpan Uptime { get; init; }
    public bool NetworkAvailable { get; init; }
}

/// <summary>
/// Mesures légères pour les tuiles « En direct » : uniquement des API Win32 instantanées (pas de WMI ni de compteurs de
/// performance). Le processeur est calculé par différence entre deux appels à GetSystemTimes.
/// </summary>
internal sealed class LiveMetrics
{
    private long _lastIdle, _lastKernel, _lastUser;
    private bool _primed;
    private readonly Lock _gate = new();

    public static string SystemDriveRoot { get; } = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";

    /// <summary>Oublie la mesure précédente (après une pause, l'écart ne serait pas représentatif).</summary>
    public void Reset()
    {
        lock (_gate) _primed = false;
    }

    public LiveSample Sample()
    {
        var s = new LiveSample
        {
            CpuRatio = SampleCpu(),
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
            NetworkAvailable = SafeNetwork(),
        };
        return Fill(s);
    }

    private static LiveSample Fill(LiveSample s)
    {
        var mem = new DashNative.MemoryStatusEx { Length = (uint)Marshal.SizeOf<DashNative.MemoryStatusEx>() };
        if (DashNative.GlobalMemoryStatusEx(ref mem))
            s = s with { RamTotal = mem.TotalPhys, RamUsed = mem.TotalPhys - Math.Min(mem.TotalPhys, mem.AvailPhys) };

        try
        {
            var drive = new DriveInfo(SystemDriveRoot);
            if (drive.IsReady)
                s = s with { DriveName = drive.Name.TrimEnd('\\'), DriveTotal = drive.TotalSize, DriveFree = drive.AvailableFreeSpace };
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        if (DashNative.GetSystemPowerStatus(out var power))
        {
            // BatteryFlag : 128 = pas de batterie, 255 = inconnu ; 8 = en charge.
            var hasBattery = power.BatteryFlag != 128 && power.BatteryFlag != 255;
            s = s with
            {
                HasBattery = hasBattery,
                BatteryPercent = hasBattery && power.BatteryLifePercent <= 100 ? power.BatteryLifePercent : null,
                OnAc = power.AcLineStatus switch { 0 => false, 1 => true, _ => null },
                Charging = hasBattery && (power.BatteryFlag & 8) != 0,
                BatteryRemaining = hasBattery && power.AcLineStatus == 0 && power.BatteryLifeTime != uint.MaxValue && power.BatteryLifeTime > 0
                    ? TimeSpan.FromSeconds(power.BatteryLifeTime) : null,
            };
        }
        return s;
    }

    private double? SampleCpu()
    {
        if (!DashNative.GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        lock (_gate)
        {
            double? ratio = null;
            if (_primed)
            {
                var dIdle = idle - _lastIdle;
                var total = kernel - _lastKernel + (user - _lastUser); // le temps noyau inclut le temps d'inactivité
                if (total > 0) ratio = Math.Clamp(1.0 - (double)dIdle / total, 0, 1);
            }
            (_lastIdle, _lastKernel, _lastUser, _primed) = (idle, kernel, user, true);
            return ratio;
        }
    }

    private static bool SafeNetwork()
    {
        try { return NetworkInterface.GetIsNetworkAvailable(); }
        catch (NetworkInformationException) { return false; }
    }
}
