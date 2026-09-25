using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Timonier.Core.Platform;

/// <summary>
/// Construit le <see cref="SystemProfile"/>. La partie Windows (registre) est synchrone et quasi instantanée ;
/// la partie matérielle (WMI, ~0,3–2 s) est chargée en arrière-plan puis mise en cache sur disque pour que
/// les lancements suivants affichent tout immédiatement.
/// </summary>
public static partial class SystemProfileService
{
    private static readonly string CachePath = Path.Combine(AppPaths.LocalData, "system-profile.json");

    public static SystemProfile LoadFast()
    {
        var p = TryLoadCache() ?? new SystemProfile();
        ReadWindowsInfo(p);
        ReadSessionInfo(p);
        return p;
    }

    /// <summary>Complète le profil avec le matériel (à appeler hors du thread UI).</summary>
    public static void LoadHardware(SystemProfile p)
    {
        Try(() => ReadComputerSystem(p));
        Try(() => ReadProcessor(p));
        Try(() => ReadGpus(p));
        Try(() => ReadBattery(p));
        Try(() => ReadDisks(p));
        Try(() => ReadDevicesPresence(p));
        Try(() => ReadSecurity(p));
        Try(() => p.HasTouch = Native.GetSystemMetrics(Native.SM_MAXIMUMTOUCHES) > 0);
        Try(() => p.HasWifi = NetworkInterface.GetAllNetworkInterfaces().Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211));
        p.Tier = ComputeTier(p);
        p.HardwareLoaded = true;
        SaveCache(p);
    }

    private static void Try(Action a)
    {
        try { a(); }
        catch (Exception ex) { Log.Warn("SystemProfile", ex.Message); }
    }

    // ------------------------------------------------------------------ Windows

    private static void ReadWindowsInfo(SystemProfile p)
    {
        using var cv = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        if (cv is null) return;
        p.Build = int.TryParse(cv.GetValue("CurrentBuild") as string, out var b) ? b : Environment.OSVersion.Version.Build;
        p.Ubr = cv.GetValue("UBR") is int u ? u : 0;
        p.DisplayVersion = cv.GetValue("DisplayVersion") as string ?? cv.GetValue("ReleaseId") as string ?? "";
        p.EditionId = cv.GetValue("EditionID") as string ?? "";
        var installationType = cv.GetValue("InstallationType") as string ?? "Client";
        p.Edition = MapEdition(p.EditionId, installationType);

        // Le registre annonce "Windows 10" même sur Windows 11 : on corrige via le numéro de build.
        var product = cv.GetValue("ProductName") as string ?? "Windows";
        if (p.Build >= 22000 && product.Contains("Windows 10", StringComparison.OrdinalIgnoreCase))
            product = product.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);
        p.ProductName = product;
        p.Architecture = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
    }

    internal static EditionFamily MapEdition(string editionId, string installationType)
    {
        if (installationType.Contains("Server", StringComparison.OrdinalIgnoreCase)) return EditionFamily.Server;
        var e = editionId.ToLowerInvariant();
        return e switch
        {
            _ when e.StartsWith("core") => EditionFamily.Home,
            _ when e.StartsWith("professionaleducation") => EditionFamily.ProEducation,
            _ when e.StartsWith("professionalworkstation") => EditionFamily.ProWorkstation,
            _ when e.StartsWith("professional") => EditionFamily.Pro,
            _ when e.StartsWith("education") => EditionFamily.Education,
            _ when e.StartsWith("iotenterprise") => EditionFamily.IoTEnterprise,
            _ when e.StartsWith("enterprise") => EditionFamily.Enterprise,
            _ when e.StartsWith("cloudedition") => EditionFamily.SE,
            _ when e.StartsWith("server") => EditionFamily.Server,
            _ => EditionFamily.Unknown,
        };
    }

    private static void ReadSessionInfo(SystemProfile p)
    {
        using var identity = WindowsIdentity.GetCurrent();
        p.UserName = Environment.UserName;
        p.MachineName = Environment.MachineName;
        p.UserSid = identity.User?.Value ?? "";
        var principal = new WindowsPrincipal(identity);
        p.IsElevated = principal.IsInRole(WindowsBuiltInRole.Administrator);
        // Membre du groupe Administrateurs même avec un jeton filtré par l'UAC ?
        p.IsUserAdmin = p.IsElevated || identity.Groups?.Any(g => g.Value == "S-1-5-32-544") == true
                        || Native.IsTokenElevationTypeLimited();
        p.IsUefi = Native.IsUefiFirmware();
        p.IsDomainJoined = !string.IsNullOrEmpty(IPGlobalProperties.GetIPGlobalProperties().DomainName);
        using (var join = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CloudDomainJoin\JoinInfo"))
            p.IsEntraJoined = join?.SubKeyCount > 0;
        p.IsMdmManaged = DetectMdm();
    }

    private static bool DetectMdm()
    {
        using var enroll = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Enrollments");
        if (enroll is null) return false;
        foreach (var name in enroll.GetSubKeyNames())
        {
            using var k = enroll.OpenSubKey(name);
            if (k?.GetValue("EnrollmentState") is int state && state == 1 && k.GetValue("UPN") is string upn && upn.Length > 0)
                return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ Matériel (WMI)

    private static IEnumerable<ManagementObject> Wmi(string query, string scope = @"root\cimv2")
    {
        using var searcher = new ManagementObjectSearcher(scope, query) { Options = { Timeout = TimeSpan.FromSeconds(10), ReturnImmediately = true } };
        foreach (ManagementObject mo in searcher.Get())
            using (mo) yield return mo;
    }

    private static void ReadComputerSystem(SystemProfile p)
    {
        foreach (var cs in Wmi("SELECT Manufacturer, Model, PCSystemType, TotalPhysicalMemory, PartOfDomain, HypervisorPresent FROM Win32_ComputerSystem"))
        {
            p.ManufacturerRaw = (cs["Manufacturer"] as string ?? "").Trim();
            p.Model = (cs["Model"] as string ?? "").Trim();
            p.RamGb = Math.Round(Convert.ToDouble(cs["TotalPhysicalMemory"] ?? 0) / (1024d * 1024 * 1024), 1);
            p.IsDomainJoined |= cs["PartOfDomain"] is true;
            var pcType = Convert.ToInt32(cs["PCSystemType"] ?? 0);
            p.FormFactor = pcType switch { 1 => FormFactor.Desktop, 2 => FormFactor.Laptop, 3 => FormFactor.Desktop, 4 or 5 or 7 => FormFactor.Server, 8 => FormFactor.Tablet, _ => FormFactor.Unknown };
        }
        p.Manufacturer = NormalizeManufacturer(p.ManufacturerRaw);
        p.IsVirtualMachine = IsVm(p.ManufacturerRaw, p.Model);

        // Le type de châssis est plus précis que PCSystemType (convertible, tout-en-un, mini PC…).
        foreach (var enc in Wmi("SELECT ChassisTypes FROM Win32_SystemEnclosure"))
        {
            if (enc["ChassisTypes"] is ushort[] types && types.Length > 0)
            {
                var chassis = types[0] switch
                {
                    3 or 4 or 5 or 6 or 7 or 15 or 16 => FormFactor.Desktop,
                    8 or 9 or 10 or 14 => FormFactor.Laptop,
                    31 or 32 => FormFactor.Convertible,
                    30 => FormFactor.Tablet,
                    13 => FormFactor.AllInOne,
                    35 or 36 => FormFactor.MiniPc,
                    17 or 23 or 28 => FormFactor.Server,
                    _ => FormFactor.Unknown,
                };
                if (chassis != FormFactor.Unknown) p.FormFactor = chassis;
            }
        }
        if (p.IsVirtualMachine) p.FormFactor = FormFactor.VirtualMachine;
    }

    internal static string NormalizeManufacturer(string raw)
    {
        var r = raw.ToLowerInvariant();
        if (r.Length == 0 || r.Contains("to be filled") || r.Contains("system manufacturer") || r == "default string" || r == "o.e.m.")
            return "Fabricant non renseigné";
        (string needle, string brand)[] map =
        [
            ("lenovo", "Lenovo"), ("hewlett", "HP"), ("hp", "HP"), ("dell", "Dell"), ("asus", "ASUS"), ("acer", "Acer"),
            ("micro-star", "MSI"), ("msi", "MSI"), ("microsoft", "Microsoft"), ("samsung", "Samsung"), ("toshiba", "Toshiba"),
            ("dynabook", "Dynabook"), ("fujitsu", "Fujitsu"), ("gigabyte", "Gigabyte"), ("huawei", "Huawei"), ("xiaomi", "Xiaomi"),
            ("razer", "Razer"), ("lg electronics", "LG"), ("medion", "Medion"), ("apple", "Apple"), ("framework", "Framework"),
            ("thomson", "Thomson"), ("schneider", "Schneider"), ("chuwi", "Chuwi"), ("teclast", "Teclast"), ("minisforum", "Minisforum"),
            ("beelink", "Beelink"), ("intel", "Intel"), ("vmware", "VMware"), ("innotek", "VirtualBox"), ("qemu", "QEMU"), ("parallels", "Parallels"),
        ];
        foreach (var (needle, brand) in map)
            if (r.Contains(needle)) return brand;
        return raw;
    }

    private static bool IsVm(string manufacturer, string model)
    {
        var s = (manufacturer + " " + model).ToLowerInvariant();
        return s.Contains("virtual machine") || s.Contains("vmware") || s.Contains("virtualbox") || s.Contains("innotek")
               || s.Contains("qemu") || s.Contains("kvm") || s.Contains("parallels") || s.Contains("hvm domu");
    }

    private static void ReadProcessor(SystemProfile p)
    {
        // Le profil peut venir du cache disque : on recompte depuis zéro (plusieurs sockets s'additionnent).
        int cores = 0, threads = 0;
        foreach (var cpu in Wmi("SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor"))
        {
            p.CpuName = Regex.Replace((cpu["Name"] as string ?? "").Trim(), @"\s{2,}", " ");
            var m = (cpu["Manufacturer"] as string ?? "").ToLowerInvariant();
            p.CpuVendor = m.Contains("intel") ? HardwareVendor.Intel : m.Contains("amd") ? HardwareVendor.Amd
                        : m.Contains("qualcomm") || p.Architecture == "arm64" ? HardwareVendor.Qualcomm : HardwareVendor.Other;
            cores += Convert.ToInt32(cpu["NumberOfCores"] ?? 0);
            threads += Convert.ToInt32(cpu["NumberOfLogicalProcessors"] ?? 0);
        }
        p.CpuCores = cores;
        p.CpuThreads = threads > 0 ? threads : Environment.ProcessorCount;
    }

    private static void ReadGpus(SystemProfile p)
    {
        var list = new List<GpuInfo>();
        foreach (var gpu in Wmi("SELECT Name, PNPDeviceID, DriverVersion FROM Win32_VideoController"))
        {
            var pnp = (gpu["PNPDeviceID"] as string ?? "").ToUpperInvariant();
            var name = (gpu["Name"] as string ?? "").Trim();
            var vendor = pnp.Contains("VEN_8086") ? HardwareVendor.Intel
                       : pnp.Contains("VEN_10DE") ? HardwareVendor.Nvidia
                       : pnp.Contains("VEN_1002") || pnp.Contains("VEN_1022") ? HardwareVendor.Amd
                       : pnp.Contains("QCOM") || name.Contains("Adreno", StringComparison.OrdinalIgnoreCase) ? HardwareVendor.Qualcomm
                       : name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase) ? HardwareVendor.Microsoft
                       : pnp.Contains("VEN_15AD") || pnp.Contains("VEN_80EE") || pnp.Contains("VEN_1414") ? HardwareVendor.Virtual
                       : HardwareVendor.Other;
            var integrated = vendor is HardwareVendor.Intel or HardwareVendor.Qualcomm
                             && !name.Contains("Arc A", StringComparison.OrdinalIgnoreCase)
                             || vendor == HardwareVendor.Amd && Regex.IsMatch(name, @"Radeon\(TM\) Graphics|Vega \d|Radeon \d{3}M", RegexOptions.IgnoreCase);
            list.Add(new GpuInfo { Name = name, Vendor = vendor, Integrated = integrated, DriverVersion = gpu["DriverVersion"] as string });
        }
        p.Gpus = list;
    }

    private static void ReadBattery(SystemProfile p)
    {
        p.HasBattery = Wmi("SELECT BatteryStatus FROM Win32_Battery").Any();
        if (p.HasBattery && p.FormFactor is FormFactor.Unknown or FormFactor.Desktop) p.FormFactor = FormFactor.Laptop;
    }

    private static void ReadDisks(SystemProfile p)
    {
        const string storage = @"root\Microsoft\Windows\Storage";
        var systemLetter = char.ToUpperInvariant(Environment.GetFolderPath(Environment.SpecialFolder.Windows)[0]);
        uint? systemDisk = null;
        foreach (var part in Wmi($"SELECT DiskNumber FROM MSFT_Partition WHERE DriveLetter = '{systemLetter}'", storage))
            systemDisk = Convert.ToUInt32(part["DiskNumber"]);

        var disks = new List<DiskInfo>();
        foreach (var d in Wmi("SELECT DeviceId, FriendlyName, MediaType, BusType, Size, HealthStatus FROM MSFT_PhysicalDisk", storage))
        {
            var bus = Convert.ToInt32(d["BusType"] ?? 0);
            var media = Convert.ToInt32(d["MediaType"] ?? 0);
            disks.Add(new DiskInfo
            {
                Model = (d["FriendlyName"] as string ?? "").Trim(),
                Bus = bus switch { 17 => "NVMe", 11 => "SATA", 7 => "USB", 8 => "RAID", 10 => "SAS", 12 => "SD", 13 => "MMC", 15 => "Virtuel", _ => "Autre" },
                Media = bus == 17 ? DiskMedia.Nvme : media switch { 3 => DiskMedia.Hdd, 4 => DiskMedia.Ssd, _ => bus is 12 or 13 ? DiskMedia.Ssd : DiskMedia.Unknown },
                SizeBytes = Convert.ToInt64(d["Size"] ?? 0L),
                IsSystemDisk = systemDisk.HasValue && d["DeviceId"] as string == systemDisk.Value.ToString(),
                Health = Convert.ToInt32(d["HealthStatus"] ?? -1) switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => "Unknown" },
            });
        }
        p.Disks = disks;
    }

    private static void ReadDevicesPresence(SystemProfile p)
    {
        // Recompté depuis zéro : le profil peut venir du cache (matériel retiré depuis).
        bool bluetooth = false, camera = false;
        foreach (var dev in Wmi("SELECT PNPClass FROM Win32_PnPEntity WHERE PNPClass = 'Bluetooth' OR PNPClass = 'Camera' OR PNPClass = 'Image'"))
        {
            var cls = dev["PNPClass"] as string;
            if (cls == "Bluetooth") bluetooth = true;
            else camera = true;
        }
        p.HasBluetooth = bluetooth;
        p.HasCamera = camera;
    }

    private static void ReadSecurity(SystemProfile p)
    {
        using var sb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
        p.SecureBoot = sb?.GetValue("UEFISecureBootEnabled") is int v ? v == 1 : p.IsUefi ? null : false;
    }

    private static PerformanceTier ComputeTier(SystemProfile p)
    {
        var lowCpu = Regex.IsMatch(p.CpuName, @"Celeron|Pentium|Atom|Athlon (Silver|Gold)|\bN\d{3,4}\b|\bN\d{2}\b|A[46]-|E[12]-|Snapdragon 7c", RegexOptions.IgnoreCase);
        var hasDiscreteGpu = p.Gpus.Any(g => !g.Integrated && g.Vendor is HardwareVendor.Nvidia or HardwareVendor.Amd);
        if (p.RamGb is > 0 and < 6 || lowCpu || p.CpuThreads is > 0 and <= 2 || p.SystemDiskIsHdd) return PerformanceTier.Low;
        if (p.RamGb >= 16 && p.CpuThreads >= 8 && (hasDiscreteGpu || p.CpuThreads >= 12)) return PerformanceTier.High;
        return p.RamGb > 0 ? PerformanceTier.Medium : PerformanceTier.Unknown;
    }

    // ------------------------------------------------------------------ Cache

    private static SystemProfile? TryLoadCache()
    {
        try
        {
            if (!File.Exists(CachePath)) return null;
            var cached = JsonSerializer.Deserialize(File.ReadAllText(CachePath), CoreJson.Default.SystemProfile);
            if (cached is null) return null;
            cached.HardwareLoaded = false; // sera rafraîchi en arrière-plan
            // Fichier modifiable à la main : pas de listes nulles (les conditions des réglages les parcourent).
            cached.Gpus ??= [];
            cached.Disks ??= [];
            cached.Gpus.RemoveAll(g => g is null);
            cached.Disks.RemoveAll(d => d is null);
            return cached;
        }
        catch { return null; }
    }

    private static void SaveCache(SystemProfile p)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LocalData);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(p, CoreJson.Default.SystemProfile));
        }
        catch (Exception ex) { Log.Warn("SystemProfile", "cache: " + ex.Message); }
    }
}
