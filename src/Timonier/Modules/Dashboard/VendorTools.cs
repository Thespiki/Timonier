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
                        L("Up-to-date drivers and BIOS, warranty, battery conservation mode, keyboard and camera settings. On managed ThinkPads, Lenovo System Update does the same job for drivers."),
                        "Lenovo", PcGlyph, "9WZDNCRFJ4MV"));
                    break;
                case "HP":
                    list.Add(new VendorTool("HP Support Assistant", "HP",
                        L("Drivers, BIOS, hardware diagnostics and warranty for consumer HP PCs. Business models (EliteBook, ProBook, ZBook) use HP Image Assistant instead."),
                        "HP", PcGlyph));
                    break;
                case "Dell":
                    list.Add(new VendorTool(L("Dell SupportAssist and Command | Update"), "Dell",
                        L("SupportAssist: diagnostics and warranty; Command | Update: up-to-date drivers, BIOS and firmware."),
                        "Dell", PcGlyph));
                    break;
                case "ASUS":
                    list.Add(new VendorTool("MyASUS", "ASUS",
                        L("Drivers, diagnostics, battery charge limit and support for ASUS PCs."),
                        "ASUS", PcGlyph, "9N7R5S6B0ZZH"));
                    break;
                case "Acer":
                    list.Add(new VendorTool("Acer Care Center", "Acer",
                        L("Driver updates, PC health checks and warranty information for Acer PCs."),
                        "Acer", PcGlyph));
                    break;
                case "MSI":
                    list.Add(new VendorTool("MSI Center", "MSI",
                        L("Performance and fan profiles, hardware monitoring and updates for MSI PCs."),
                        "MSI Center", PcGlyph));
                    break;
                case "Microsoft" when p.Model.Contains("Surface", StringComparison.OrdinalIgnoreCase):
                    list.Add(new VendorTool("Surface", "Microsoft",
                        L("Battery status, pen and accessories, warranty and settings specific to Surface devices."),
                        "Surface", PcGlyph, "9WZDNCRFJB8P"));
                    break;
            }
        }

        var vendors = p.Gpus.Select(g => g.Vendor).ToHashSet();
        if (vendors.Contains(HardwareVendor.Nvidia))
            list.Add(new VendorTool("NVIDIA App", "NVIDIA",
                L("Game Ready or Studio drivers, 3D settings and game optimization for GeForce graphics cards."),
                "NVIDIA", GpuGlyph));
        if (vendors.Contains(HardwareVendor.Amd))
            list.Add(new VendorTool("AMD Software: Adrenalin Edition", "AMD",
                L("Drivers and settings for Radeon graphics cards and chips (performance, capture, display)."),
                "AMD", GpuGlyph));
        if (vendors.Contains(HardwareVendor.Intel))
            list.Add(new VendorTool("Intel Driver & Support Assistant", "Intel",
                L("Detects newer Intel drivers (graphics, Wi-Fi, Bluetooth). On a laptop, drivers customized by the PC manufacturer are sometimes still preferable."),
                "Intel Driver", DriverGlyph));
        return list;
    }
}
