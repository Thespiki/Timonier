using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Microsoft.Win32;
using Timonier.Core.Model;
using Timonier.Core.Platform;

namespace Timonier.Modules.Startup;

/// <summary>Service Win32 (pilotes exclus) avec sa configuration lue dans le registre.</summary>
public sealed class ServiceItem
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public string Description { get; init; } = "";
    public ServiceControllerStatus Status { get; set; }
    public ServiceStartKind? Start { get; set; }
    /// <summary>Nom à configurer : le service lui-même, ou son modèle pour une instance par utilisateur (ex. CDPUserSvc_1a2b3).</summary>
    public required string ConfigName { get; init; }
    public bool IsPerUserInstance { get; init; }
    public bool IsMicrosoft { get; init; }
    public string? Company { get; init; }
    public string? ExePath { get; init; }
    public string ImagePath { get; init; } = "";
    public string? Account { get; init; }
    public bool IsProtected { get; init; }

    public bool IsRunning => Status == ServiceControllerStatus.Running;
    public bool IsDisabled => Start == ServiceStartKind.Disabled;
    public bool CanConfigure => !IsProtected && Start is ServiceStartKind.Automatic or ServiceStartKind.AutomaticDelayed
                                                          or ServiceStartKind.Manual or ServiceStartKind.Disabled;

    public string StatusLabel => Status switch
    {
        ServiceControllerStatus.Running => "En cours",
        ServiceControllerStatus.Stopped => "Arrêté",
        ServiceControllerStatus.Paused => "Suspendu",
        ServiceControllerStatus.StartPending => "Démarrage…",
        ServiceControllerStatus.StopPending => "Arrêt…",
        _ => "En transition",
    };

    public string AccountLabel => Account?.ToLowerInvariant() switch
    {
        null or "" => "",
        "localsystem" => "Système local",
        @"nt authority\localservice" => "Service local",
        @"nt authority\networkservice" => "Service réseau",
        _ => Account!,
    };
}

/// <summary>
/// Inventaire des services (lecture seule, sans élévation) et règles de protection partagées avec les actions admin.
/// </summary>
public static partial class ServiceInventory
{
    private const string ServicesKey = @"SYSTEM\CurrentControlSet\Services\";

    /// <summary>
    /// Services indispensables au démarrage, à la session, au réseau, aux mises à jour ou à la sécurité de Windows :
    /// leur type de démarrage ne peut pas être modifié depuis Timonier (liste fermée, vérifiée aussi dans le broker).
    /// </summary>
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Noyau RPC/COM, session et profils
        "RpcSs", "RpcEptMapper", "DcomLaunch", "LSM", "SamSs", "ProfSvc", "UserManager", "gpsvc", "Power", "PlugPlay",
        "EventLog", "EventSystem", "SENS", "Winmgmt", "Schedule", "CoreMessagingRegistrar", "BrokerInfrastructure",
        "SystemEventsBroker", "TimeBrokerSvc", "StateRepository", "TextInputManagementService",
        // Sécurité (ne jamais affaiblir)
        "CryptSvc", "BFE", "MpsSvc", "WinDefend", "WdNisSvc", "Sense", "MDCoreSvc", "SecurityHealthService", "wscsvc",
        "SgrmBroker", "webthreatdefsvc", "webthreatdefusersvc", "KeyIso", "VaultSvc", "Appinfo", "NgcSvc", "NgcCtnrSvc",
        // BitLocker ; wlidsvc : ouverture de session avec un compte Microsoft (selon la description du service)
        "BDESVC", "wlidsvc",
        // Réseau de base (Netlogon : ouverture de session sur un PC joint à un domaine)
        "Dhcp", "Dnscache", "nsi", "NlaSvc", "netprofm", "LanmanWorkstation", "Wcmsvc", "WinHttpAutoProxySvc", "Netlogon",
        // Mises à jour, installation et licences
        "TrustedInstaller", "wuauserv", "UsoSvc", "WaaSMedicSvc", "BITS", "DoSvc", "sppsvc", "ClipSVC", "AppXSvc", "AppReadiness",
        // Son
        "Audiosrv", "AudioEndpointBuilder",
    };

    public static bool IsProtected(string name) => ProtectedNames.Contains(name) || ProtectedNames.Contains(TemplateName(name));

    /// <summary>« CDPUserSvc_1a2b3c » → « CDPUserSvc ».</summary>
    public static string TemplateName(string name)
    {
        var i = name.LastIndexOf('_');
        return i > 0 ? name[..i] : name;
    }

    /// <summary>
    /// Vérifie qu'un service peut être piloté par Timonier : existe, n'est pas un pilote, n'est pas protégé.
    /// Renvoie le nom à configurer (le modèle pour une instance par utilisateur), sinon lève une ValidationException.
    /// </summary>
    public static string EnsureManageable(string name, bool forStartType)
    {
        if (!ServiceConfig.IsValidName(name)) throw new Core.Security.ValidationException("Nom de service invalide.");
        int type;
        using (var k = Registry.LocalMachine.OpenSubKey(ServicesKey + name, false))
        {
            if (k is null) throw new Core.Security.ValidationException($"Le service « {name} » n'existe pas sur ce PC.");
            type = k.GetValue("Type") is int t ? t : 0;
        }
        if ((type & 0x30) == 0) throw new Core.Security.ValidationException("Les pilotes ne sont pas gérés ici.");
        if (IsProtected(name))
            throw new Core.Security.ValidationException("Ce service est protégé : il est indispensable au démarrage, au réseau, aux mises à jour ou à la sécurité de Windows.");
        if (!forStartType || (type & 0x80) == 0) return name;

        // Instance par utilisateur : le type de démarrage se règle sur le service modèle.
        var template = TemplateName(name);
        if (!ServiceConfig.IsValidName(template) || !ServiceConfig.Exists(template))
            throw new Core.Security.ValidationException("Service modèle introuvable pour cette instance par utilisateur.");
        return template;
    }

    public static ServiceStartKind ParseStart(string value) => value.ToLowerInvariant() switch
    {
        "auto" => ServiceStartKind.Automatic,
        "delayed" => ServiceStartKind.AutomaticDelayed,
        "manual" => ServiceStartKind.Manual,
        "disabled" => ServiceStartKind.Disabled,
        _ => throw new Core.Security.ValidationException("Type de démarrage non autorisé."),
    };

    public static string StartParam(ServiceStartKind kind) => kind switch
    {
        ServiceStartKind.Automatic => "auto",
        ServiceStartKind.AutomaticDelayed => "delayed",
        ServiceStartKind.Manual => "manual",
        _ => "disabled",
    };

    public static string StartLabel(ServiceStartKind? kind) => kind switch
    {
        ServiceStartKind.Automatic => "Automatique",
        ServiceStartKind.AutomaticDelayed => "Automatique (différé)",
        ServiceStartKind.Manual => "Manuel",
        ServiceStartKind.Disabled => "Désactivé",
        ServiceStartKind.Boot or ServiceStartKind.System => "Démarrage du noyau",
        _ => "Inconnu",
    };

    /// <summary>Énumère les services Win32 (lent : à appeler hors du thread UI).</summary>
    public static List<ServiceItem> Load()
    {
        var result = new List<ServiceItem>();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var companyCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var controllers = ServiceController.GetServices();
        try
        {
            foreach (var sc in controllers)
            {
                try
                {
                    var name = sc.ServiceName;
                    using var k = Registry.LocalMachine.OpenSubKey(ServicesKey + name, false);
                    if (k is null) continue;
                    var type = k.GetValue("Type") is int t ? t : 0;
                    if ((type & 0x30) == 0) continue; // pilote
                    var instance = (type & 0x80) != 0;
                    var configName = instance ? TemplateName(name) : name;
                    var image = k.GetValue("ImagePath", "", RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? "";
                    var exe = ResolveImage(image, windows);
                    string? company = null;
                    if (exe is not null && !companyCache.TryGetValue(exe, out company))
                    {
                        try { company = FileVersionInfo.GetVersionInfo(exe).CompanyName?.Trim(); } catch { company = null; }
                        companyCache[exe] = company;
                    }
                    var isMicrosoft = IsMicrosoftImage(exe, image, company, windows);
                    ServiceStartKind? start = ServiceConfig.ReadStart(configName);
                    string display;
                    try { display = sc.DisplayName; } catch { display = name; }
                    // « Accès aux données utilisateur_709cc5 » → « Accès aux données utilisateur » (le suffixe identifie la session).
                    if (instance && name.Length > configName.Length && display.EndsWith(name[configName.Length..], StringComparison.OrdinalIgnoreCase))
                        display = display[..^(name.Length - configName.Length)];
                    ServiceControllerStatus status;
                    try { status = sc.Status; } catch { status = ServiceControllerStatus.Stopped; }

                    result.Add(new ServiceItem
                    {
                        Name = name,
                        DisplayName = string.IsNullOrWhiteSpace(display) ? name : display,
                        Description = ResolveIndirect(k.GetValue("Description") as string) ?? "",
                        Status = status,
                        Start = start,
                        ConfigName = configName,
                        IsPerUserInstance = instance,
                        IsMicrosoft = isMicrosoft,
                        Company = company,
                        ExePath = exe,
                        ImagePath = image,
                        Account = k.GetValue("ObjectName") as string,
                        IsProtected = IsProtected(name),
                    });
                }
                catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    // service disparu pendant l'énumération ou clé illisible
                }
            }
        }
        finally
        {
            foreach (var sc in controllers) sc.Dispose();
        }
        result.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase));
        return result;
    }

    /// <summary>État actuel d'un seul service (après une action), sans réénumérer.</summary>
    public static (ServiceControllerStatus Status, ServiceStartKind? Start) Refresh(ServiceItem item)
    {
        ServiceControllerStatus status;
        try
        {
            using var sc = new ServiceController(item.Name);
            status = sc.Status;
        }
        catch { status = ServiceControllerStatus.Stopped; }
        return (status, ServiceConfig.ReadStart(item.ConfigName));
    }

    private static string? ResolveImage(string imagePath, string windows)
    {
        var p = imagePath.Trim();
        if (p.Length == 0) return null;
        if (p.StartsWith(@"\??\", StringComparison.Ordinal)) p = p[4..];
        if (p.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase)) p = Path.Combine(windows, p[12..]);
        else if (p.StartsWith(@"system32\", StringComparison.OrdinalIgnoreCase)) p = Path.Combine(windows, p);
        return StartupInventory.ResolveExecutable(p);
    }

    /// <summary>
    /// Service Microsoft : éditeur « Microsoft », ou binaire du dossier Windows sans éditeur tiers (les pilotes tiers
    /// installés dans DriverStore ou System32 sont reconnus par leur éditeur).
    /// </summary>
    private static bool IsMicrosoftImage(string? exe, string rawImage, string? company, string windows)
    {
        if (!string.IsNullOrEmpty(company)) return company.Contains("Microsoft", StringComparison.OrdinalIgnoreCase);
        if (exe is not null)
            return exe.StartsWith(windows + "\\", StringComparison.OrdinalIgnoreCase)
                   && !exe.Contains(@"\DriverStore\", StringComparison.OrdinalIgnoreCase);
        return rawImage.Contains("%SystemRoot%", StringComparison.OrdinalIgnoreCase)
               || rawImage.Contains("%windir%", StringComparison.OrdinalIgnoreCase);
    }

    [LibraryImport("shlwapi.dll", EntryPoint = "SHLoadIndirectString", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHLoadIndirectString(string source, [Out] char[] buffer, uint bufferSize, nint reserved);

    /// <summary>Résout une chaîne indirecte « @%SystemRoot%\system32\x.dll,-123 » (ou « $(@…) » du Planificateur).</summary>
    public static string? ResolveIndirect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var v = value.Trim();
        if (v.StartsWith("$(@", StringComparison.Ordinal) && v.EndsWith(')')) v = v[2..^1];
        if (!v.StartsWith('@')) return value;
        try
        {
            var buffer = new char[1024];
            var hr = SHLoadIndirectString(Environment.ExpandEnvironmentVariables(v), buffer, (uint)buffer.Length, 0);
            if (hr != 0) return null;
            var end = Array.IndexOf(buffer, '\0');
            return new string(buffer, 0, end < 0 ? buffer.Length : end);
        }
        catch { return null; }
    }
}
