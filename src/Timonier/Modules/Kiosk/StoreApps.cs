using System.Windows.Media;
using System.Windows.Media.Imaging;
using Timonier.Core.Platform;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace Timonier.Modules.Kiosk;

/// <summary>Application empaquetée (Store / MSIX) utilisable en accès attribué.</summary>
internal sealed record StoreApp(string Name, string Aumid, string Publisher, string? LogoPath)
{
    public ImageSource? Logo { get; set; }
}

/// <summary>Énumère les applications empaquetées de l'utilisateur courant (sans élévation, hors thread UI).</summary>
internal static class StoreApps
{
    public static List<StoreApp> Load()
    {
        var result = new Dictionary<string, StoreApp>(StringComparer.OrdinalIgnoreCase);
        var pm = new PackageManager();
        foreach (var package in pm.FindPackagesForUser(string.Empty))
        {
            try
            {
                if (package.IsFramework || package.IsResourcePackage || package.IsBundle) continue;
                if (package.SignatureKind == PackageSignatureKind.System) continue; // composants de Windows (Paramètres, shell…)
                string? logo = null;
                try
                {
                    var uri = package.Logo;
                    if (uri is not null && uri.IsFile && File.Exists(uri.LocalPath)) logo = uri.LocalPath;
                }
                catch { /* logo indisponible */ }
                string publisher = "";
                try { publisher = package.PublisherDisplayName; } catch { }
                foreach (var entry in package.GetAppListEntries())
                {
                    var aumid = entry.AppUserModelId;
                    var name = entry.DisplayInfo?.DisplayName;
                    if (string.IsNullOrWhiteSpace(aumid) || string.IsNullOrWhiteSpace(name) || name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) continue;
                    try { Core.Security.Validate.Aumid(aumid); } catch { continue; }
                    result.TryAdd(aumid, new StoreApp(name, aumid, publisher, logo));
                }
            }
            catch (Exception ex) { Log.Warn("Kiosk", "paquet ignoré : " + ex.Message); }
        }
        var list = result.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        foreach (var app in list)
        {
            if (app.LogoPath is null) continue;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(app.LogoPath);
                bmp.DecodePixelWidth = 32;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                app.Logo = bmp;
            }
            catch { /* logo illisible : icône générique */ }
        }
        return list;
    }
}
