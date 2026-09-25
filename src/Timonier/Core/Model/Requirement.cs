using Timonier.Core.Platform;

namespace Timonier.Core.Model;

/// <summary>
/// Conditions de disponibilité d'une fonctionnalité. <see cref="Check"/> renvoie la raison (en français)
/// pour laquelle elle est indisponible sur ce PC, ou null : c'est affiché tel quel à l'utilisateur.
/// </summary>
public sealed record Requirement
{
    public static readonly Requirement None = new();

    public int? MinBuild { get; init; }
    public int? MaxBuild { get; init; }
    public EditionFamily[]? Editions { get; init; }
    public bool NeedsBattery { get; init; }
    public bool NeedsTouch { get; init; }
    public bool NeedsBluetooth { get; init; }
    public bool NeedsWifi { get; init; }
    public bool NeedsCamera { get; init; }
    public bool NotOnVirtualMachine { get; init; }
    public bool NotWhenManaged { get; init; }
    public HardwareVendor[]? GpuVendors { get; init; }
    public HardwareVendor[]? CpuVendors { get; init; }
    public Func<SystemProfile, bool>? Custom { get; init; }
    public string? CustomReason { get; init; }

    public string? Check(SystemProfile p)
    {
        if (MinBuild is { } min && p.Build < min)
            return min >= 22000 ? $"Nécessite Windows 11 (build {min} ou plus récent)." : $"Nécessite Windows build {min} ou plus récent.";
        if (MaxBuild is { } max && p.Build > max)
            return max < 22000 ? "Réservé à Windows 10." : $"Plus disponible après le build {max}.";
        if (Editions is { Length: > 0 } && !Editions.Contains(p.Edition))
            return $"Non disponible sur l'édition {p.EditionLabel}.";
        if (NotWhenManaged && p.IsManaged)
            return "Ce PC est géré par une organisation (domaine/MDM) : ce réglage serait écrasé par ses stratégies.";
        // Les conditions matérielles ne sont évaluées qu'une fois le matériel détecté.
        if (p.HardwareLoaded)
        {
            if (NeedsBattery && !p.HasBattery) return "Ce PC n'a pas de batterie.";
            if (NeedsTouch && !p.HasTouch) return "Ce PC n'a pas d'écran tactile.";
            if (NeedsBluetooth && !p.HasBluetooth) return "Aucun adaptateur Bluetooth détecté.";
            if (NeedsWifi && !p.HasWifi) return "Aucune carte Wi-Fi détectée.";
            if (NeedsCamera && !p.HasCamera) return "Aucune caméra détectée.";
            if (NotOnVirtualMachine && p.IsVirtualMachine) return "Sans effet dans une machine virtuelle.";
            if (GpuVendors is { Length: > 0 } && !p.Gpus.Any(g => GpuVendors.Contains(g.Vendor)))
                return $"Nécessite une carte graphique {string.Join(" / ", GpuVendors)}.";
            if (CpuVendors is { Length: > 0 } && !CpuVendors.Contains(p.CpuVendor))
                return $"Nécessite un processeur {string.Join(" / ", CpuVendors)}.";
        }
        if (Custom is not null && !Custom(p))
            return CustomReason ?? "Non disponible sur cette configuration.";
        return null;
    }

    public Requirement And(Requirement other) => this with
    {
        MinBuild = Max(MinBuild, other.MinBuild),
        MaxBuild = Min(MaxBuild, other.MaxBuild),
        Editions = Editions is null ? other.Editions : other.Editions is null ? Editions : Editions.Intersect(other.Editions).ToArray(),
        NeedsBattery = NeedsBattery || other.NeedsBattery,
        NeedsTouch = NeedsTouch || other.NeedsTouch,
        NeedsBluetooth = NeedsBluetooth || other.NeedsBluetooth,
        NeedsWifi = NeedsWifi || other.NeedsWifi,
        NeedsCamera = NeedsCamera || other.NeedsCamera,
        NotOnVirtualMachine = NotOnVirtualMachine || other.NotOnVirtualMachine,
        NotWhenManaged = NotWhenManaged || other.NotWhenManaged,
        GpuVendors = GpuVendors ?? other.GpuVendors,
        CpuVendors = CpuVendors ?? other.CpuVendors,
        Custom = Custom is null ? other.Custom : other.Custom is null ? Custom : p => Custom(p) && other.Custom(p),
        CustomReason = CustomReason ?? other.CustomReason,
    };

    private static int? Max(int? a, int? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);
    private static int? Min(int? a, int? b) => a is null ? b : b is null ? a : Math.Min(a.Value, b.Value);
}

/// <summary>Conditions courantes, prêtes à l'emploi.</summary>
public static class Requires
{
    public static readonly Requirement Windows11 = new() { MinBuild = 22000 };
    /// <summary>Windows 11 22H2 (menus, barre des tâches récente).</summary>
    public static readonly Requirement Windows11_22H2 = new() { MinBuild = 22621 };
    /// <summary>Windows 11 24H2 et plus (Copilot+, Recall, nouvelles options).</summary>
    public static readonly Requirement Windows11_24H2 = new() { MinBuild = 26100 };
    public static readonly Requirement Windows10Only = new() { MaxBuild = 21999 };

    public static readonly Requirement ProOrHigher = new()
    {
        Editions = [EditionFamily.Pro, EditionFamily.ProEducation, EditionFamily.ProWorkstation, EditionFamily.Education,
                    EditionFamily.Enterprise, EditionFamily.IoTEnterprise, EditionFamily.Server],
    };

    public static readonly Requirement EnterpriseOrEducation = new()
    {
        Editions = [EditionFamily.Education, EditionFamily.Enterprise, EditionFamily.IoTEnterprise, EditionFamily.Server],
    };

    public static readonly Requirement Battery = new() { NeedsBattery = true };
    public static readonly Requirement Touch = new() { NeedsTouch = true };
    public static readonly Requirement Bluetooth = new() { NeedsBluetooth = true };
    public static readonly Requirement Wifi = new() { NeedsWifi = true };
    public static readonly Requirement Camera = new() { NeedsCamera = true };
    public static readonly Requirement PhysicalMachine = new() { NotOnVirtualMachine = true };
    public static readonly Requirement NvidiaGpu = new() { GpuVendors = [HardwareVendor.Nvidia] };
    public static readonly Requirement AmdGpu = new() { GpuVendors = [HardwareVendor.Amd] };
    public static readonly Requirement IntelGpu = new() { GpuVendors = [HardwareVendor.Intel] };
    public static readonly Requirement Unmanaged = new() { NotWhenManaged = true };

    public static Requirement When(Func<SystemProfile, bool> predicate, string reason) => new() { Custom = predicate, CustomReason = reason };
}
