using System.Text.Json.Serialization;

namespace Timonier.Core.Platform;

public enum EditionFamily { Unknown, Home, Pro, ProEducation, ProWorkstation, Education, Enterprise, IoTEnterprise, SE, Server }
public enum FormFactor { Unknown, Desktop, Laptop, Convertible, Tablet, AllInOne, MiniPc, Server, VirtualMachine }
public enum HardwareVendor { Unknown, Intel, Amd, Nvidia, Qualcomm, Microsoft, Virtual, Other }
public enum DiskMedia { Unknown, Hdd, Ssd, Nvme }
public enum PerformanceTier { Unknown, Low, Medium, High }

public sealed class GpuInfo
{
    public string Name { get; set; } = "";
    public HardwareVendor Vendor { get; set; }
    public bool Integrated { get; set; }
    public string? DriverVersion { get; set; }
}

public sealed class DiskInfo
{
    public string Model { get; set; } = "";
    public DiskMedia Media { get; set; }
    public string Bus { get; set; } = "";
    public long SizeBytes { get; set; }
    public bool IsSystemDisk { get; set; }
    /// <summary>Healthy / Warning / Unhealthy / Unknown (MSFT_PhysicalDisk.HealthStatus).</summary>
    public string Health { get; set; } = "Unknown";
}

/// <summary>
/// Portrait du PC : édition/version de Windows (lecture registre, instantanée) et matériel (WMI, en arrière-plan,
/// mis en cache). Toutes les fonctionnalités s'adaptent à ce profil via <see cref="Model.Requirement"/>.
/// </summary>
public sealed class SystemProfile
{
    // --- Windows ---
    public int Build { get; set; }
    public int Ubr { get; set; }
    public string DisplayVersion { get; set; } = "";
    public string EditionId { get; set; } = "";
    public EditionFamily Edition { get; set; }
    public string ProductName { get; set; } = "";
    public string Architecture { get; set; } = "";
    public bool IsWindows11 => Build >= 22000;
    public bool IsServer => Edition == EditionFamily.Server;

    // --- Matériel ---
    public bool HardwareLoaded { get; set; }
    public string Manufacturer { get; set; } = "";
    public string ManufacturerRaw { get; set; } = "";
    public string Model { get; set; } = "";
    public FormFactor FormFactor { get; set; }
    public bool HasBattery { get; set; }
    public string CpuName { get; set; } = "";
    public HardwareVendor CpuVendor { get; set; }
    public int CpuCores { get; set; }
    public int CpuThreads { get; set; }
    public double RamGb { get; set; }
    public List<GpuInfo> Gpus { get; set; } = [];
    public List<DiskInfo> Disks { get; set; } = [];
    public bool HasTouch { get; set; }
    public bool IsVirtualMachine { get; set; }
    public bool HasBluetooth { get; set; }
    public bool HasWifi { get; set; }
    public bool HasCamera { get; set; }
    public PerformanceTier Tier { get; set; }

    // --- Sécurité / gestion ---
    public bool? SecureBoot { get; set; }
    public bool IsUefi { get; set; }
    public bool IsDomainJoined { get; set; }
    public bool IsEntraJoined { get; set; }
    public bool IsMdmManaged { get; set; }

    // --- Session ---
    public string UserName { get; set; } = "";
    public string MachineName { get; set; } = "";
    public string UserSid { get; set; } = "";
    public bool IsUserAdmin { get; set; }
    public bool IsElevated { get; set; }

    // --- Capacités dérivées (transparence : pourquoi une fonction est disponible ou non) ---
    [JsonIgnore] public bool IsHomeEdition => Edition is EditionFamily.Home or EditionFamily.SE;
    [JsonIgnore] public bool SupportsGroupPolicy => !IsHomeEdition && Edition != EditionFamily.Unknown;
    [JsonIgnore] public bool SupportsAssignedAccess => Edition is EditionFamily.Pro or EditionFamily.ProEducation or EditionFamily.ProWorkstation
                                                        or EditionFamily.Education or EditionFamily.Enterprise or EditionFamily.IoTEnterprise;
    [JsonIgnore] public bool SupportsShellLauncher => Edition is EditionFamily.Enterprise or EditionFamily.Education or EditionFamily.IoTEnterprise;
    /// <summary>Niveau de télémétrie « Sécurité » (0) : uniquement Entreprise, Éducation, IoT, Server.</summary>
    [JsonIgnore] public bool SupportsTelemetryOff => Edition is EditionFamily.Enterprise or EditionFamily.Education or EditionFamily.IoTEnterprise or EditionFamily.Server;
    [JsonIgnore] public bool SupportsBitLocker => !IsHomeEdition;
    [JsonIgnore] public bool IsLaptopLike => HasBattery || FormFactor is FormFactor.Laptop or FormFactor.Convertible or FormFactor.Tablet;
    [JsonIgnore] public DiskInfo? SystemDisk => Disks.FirstOrDefault(d => d.IsSystemDisk);
    [JsonIgnore] public bool SystemDiskIsHdd => SystemDisk?.Media == DiskMedia.Hdd;
    [JsonIgnore] public bool IsManaged => IsDomainJoined || IsEntraJoined || IsMdmManaged;

    [JsonIgnore]
    public string WindowsLabel => $"{ProductName} {DisplayVersion} (build {Build}.{Ubr})".Trim();

    [JsonIgnore]
    public string EditionLabel => Edition switch
    {
        EditionFamily.Home => "Famille",
        EditionFamily.Pro => "Professionnel",
        EditionFamily.ProEducation => "Professionnel Éducation",
        EditionFamily.ProWorkstation => "Professionnel pour stations de travail",
        EditionFamily.Education => "Éducation",
        EditionFamily.Enterprise => "Entreprise",
        EditionFamily.IoTEnterprise => "IoT Entreprise",
        EditionFamily.SE => "SE",
        EditionFamily.Server => "Server",
        _ => EditionId,
    };

    [JsonIgnore]
    public string FormFactorLabel => FormFactor switch
    {
        FormFactor.Desktop => "Ordinateur de bureau",
        FormFactor.Laptop => "Ordinateur portable",
        FormFactor.Convertible => "PC convertible",
        FormFactor.Tablet => "Tablette",
        FormFactor.AllInOne => "Tout-en-un",
        FormFactor.MiniPc => "Mini PC",
        FormFactor.Server => "Serveur",
        FormFactor.VirtualMachine => "Machine virtuelle",
        _ => HasBattery ? "Ordinateur portable" : "PC",
    };

    [JsonIgnore]
    public string TierLabel => Tier switch
    {
        PerformanceTier.Low => "Modeste",
        PerformanceTier.Medium => "Intermédiaire",
        PerformanceTier.High => "Performant",
        _ => "Inconnu",
    };
}
