using Timonier.Core.Platform;

namespace Timonier.Modules.Dashboard;

/// <summary>Outil officiel proposé selon le fabricant du PC ou de la carte graphique.</summary>
/// <param name="Search">Texte transmis à la page Applications (« search:… »).</param>
/// <param name="StoreId">Identifiant Microsoft Store quand l'outil y est publié par le fabricant.</param>
internal sealed record VendorTool(string Name, string Publisher, string Description, string Search, string Glyph, string? StoreId = null);

internal static class VendorTools
{
    private const string PcGlyph = "";
    private const string GpuGlyph = "";
    private const string DriverGlyph = "";

    public static List<VendorTool> For(SystemProfile p)
    {
        var list = new List<VendorTool>();
        if (!p.IsVirtualMachine)
        {
            switch (p.Manufacturer)
            {
                case "Lenovo":
                    list.Add(new VendorTool("Lenovo Vantage", "Lenovo",
                        "Pilotes et BIOS à jour, garantie, mode conservation de la batterie, réglages du clavier et de la caméra. " +
                        "Sur les ThinkPad gérés, Lenovo System Update fait le même travail pour les pilotes.",
                        "Lenovo", PcGlyph, "9WZDNCRFJ4MV"));
                    break;
                case "HP":
                    list.Add(new VendorTool("HP Support Assistant", "HP",
                        "Pilotes, BIOS, diagnostics matériels et garantie des PC HP grand public. Les modèles professionnels " +
                        "(EliteBook, ProBook, ZBook) utilisent plutôt HP Image Assistant.",
                        "HP", PcGlyph));
                    break;
                case "Dell":
                    list.Add(new VendorTool("Dell SupportAssist et Command | Update", "Dell",
                        "SupportAssist : diagnostics et garantie ; Command | Update : pilotes, BIOS et micrologiciels à jour.",
                        "Dell", PcGlyph));
                    break;
                case "ASUS":
                    list.Add(new VendorTool("MyASUS", "ASUS",
                        "Pilotes, diagnostics, limitation de la charge de la batterie et assistance des PC ASUS.",
                        "ASUS", PcGlyph, "9N7R5S6B0ZZH"));
                    break;
                case "Acer":
                    list.Add(new VendorTool("Acer Care Center", "Acer",
                        "Mises à jour des pilotes, vérification de l'état du PC et informations de garantie des PC Acer.",
                        "Acer", PcGlyph));
                    break;
                case "MSI":
                    list.Add(new VendorTool("MSI Center", "MSI",
                        "Profils de performance et de ventilation, surveillance du matériel et mises à jour des PC MSI.",
                        "MSI Center", PcGlyph));
                    break;
                case "Microsoft" when p.Model.Contains("Surface", StringComparison.OrdinalIgnoreCase):
                    list.Add(new VendorTool("Surface", "Microsoft",
                        "État de la batterie, stylet et accessoires, garantie et réglages propres aux appareils Surface.",
                        "Surface", PcGlyph, "9WZDNCRFJB8P"));
                    break;
            }
        }

        var vendors = p.Gpus.Select(g => g.Vendor).ToHashSet();
        if (vendors.Contains(HardwareVendor.Nvidia))
            list.Add(new VendorTool("NVIDIA App", "NVIDIA",
                "Pilotes Game Ready ou Studio, réglages 3D et optimisation des jeux pour les cartes GeForce.",
                "NVIDIA", GpuGlyph));
        if (vendors.Contains(HardwareVendor.Amd))
            list.Add(new VendorTool("AMD Software: Adrenalin Edition", "AMD",
                "Pilotes et réglages des cartes et puces graphiques Radeon (performances, capture, affichage).",
                "AMD", GpuGlyph));
        if (vendors.Contains(HardwareVendor.Intel))
            list.Add(new VendorTool("Intel Driver & Support Assistant", "Intel",
                "Détecte les pilotes Intel plus récents (graphiques, Wi-Fi, Bluetooth). Sur un portable, les pilotes personnalisés " +
                "par le fabricant du PC restent parfois préférables.",
                "Intel Driver", DriverGlyph));
        return list;
    }
}
