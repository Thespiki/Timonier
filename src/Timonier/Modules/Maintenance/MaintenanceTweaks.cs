using System.Text.RegularExpressions;
using Timonier.Core.Model;
using Timonier.Core.Platform;

namespace Timonier.Modules.Maintenance;

/// <summary>
/// Réglages déclaratifs de la maintenance : Windows Update (stratégies officielles de WindowsUpdate.admx) et
/// Assistant de stockage (valeurs de l'application Paramètres, HKCU). Aucun réglage ne désactive durablement les mises à jour.
/// </summary>
internal static partial class MaintenanceTweaks
{
    // Titres de groupe affichés ; la page compare TweakDefinition.Group à ces mêmes champs.
    public static readonly string GroupUpdates = L("Réglages de Windows Update");
    public static readonly string GroupStorage = L("Assistant de stockage");

    private const string Cat = MaintenanceModule.Category;
    private const string WuPolicy = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
    private const string AuPolicy = WuPolicy + @"\AU";
    private const string StoragePolicy = @"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy";

    private const long SmallDisk = 140L * 1024 * 1024 * 1024;

    [GeneratedRegex(@"^\d{2}H[12]$")]
    private static partial Regex DisplayVersionRx();

    public static IEnumerable<TweakDefinition> All()
    {
        // ---------------------------------------------------------------- Windows Update
        yield return Tweak.Toggle("maintenance.wu.autoreboot", L("Redémarrage automatique avec une session ouverte"),
                L("Autorise Windows Update à redémarrer le PC de lui-même pour terminer une installation alors qu'un utilisateur est connecté. Désactivé (stratégie officielle « Pas de redémarrage automatique avec des utilisateurs connectés »), Windows attend que vous redémarriez vous-même et vous le rappelle par des notifications : aucun travail non enregistré n'est perdu."))
            .In(Cat, GroupUpdates)
            .Keywords(L("redémarrage automatique, reboot, auto restart, NoAutoRebootWithLoggedOnUsers, redémarrer tout seul, windows update"))
            .WhenOn(Reg.LmDel(AuPolicy, "NoAutoRebootWithLoggedOnUsers"))
            .WhenOff(Reg.LmDword(AuPolicy, "NoAutoRebootWithLoggedOnUsers", 1))
            .Labels(L("Autorisé"), L("Bloqué"))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Requires(Requires.ProOrHigher)
            .Warning(L("Les correctifs de sécurité ne sont réellement appliqués qu'après le redémarrage : redémarrez quand Windows vous le demande."))
            .Tags("office", "family")
            .Build();

        yield return Tweak.Toggle("maintenance.wu.otherproducts", L("Mises à jour des autres produits Microsoft"),
                L("Fait installer par Windows Update les correctifs d'Office, des runtimes .NET, de Visual Studio et d'autres logiciels Microsoft. « Imposé » applique la stratégie officielle (case « Installer les mises à jour d'autres produits Microsoft ») ; « Au choix » laisse l'option « Recevoir les mises à jour d'autres produits Microsoft » des Paramètres décider."))
            .In(Cat, GroupUpdates)
            .Keywords(L("office, microsoft update, autres produits, AllowMUUpdateService, .net, correctifs office"))
            .WhenOn(Reg.LmDword(AuPolicy, "AllowMUUpdateService", 1))
            .WhenOff(Reg.LmDel(AuPolicy, "AllowMUUpdateService"))
            .Labels(L("Imposé"), L("Au choix (Paramètres)"))
            .WindowsDefault(TweakDefinition.Off)
            .Recommend(TweakDefinition.On)
            .Tags("office", "family")
            .Build();

        yield return Tweak.Toggle("maintenance.wu.drivers", L("Pilotes via Windows Update"),
                L("Laisse Windows Update installer automatiquement des pilotes de périphériques (graphique, Wi-Fi, audio…). Désactivé (stratégie « Ne pas inclure les pilotes avec les mises à jour Windows »), les pilotes ne viennent plus de Windows Update : utile si une mise à jour de pilote a provoqué un problème ou si vous installez ceux du fabricant."))
            .In(Cat, GroupUpdates)
            .Keywords(L("pilotes, drivers, ExcludeWUDriversInQualityUpdate, mise à jour pilote, driver update"))
            .WhenOn(Reg.LmDel(WuPolicy, "ExcludeWUDriversInQualityUpdate"))
            .WhenOff(Reg.LmDword(WuPolicy, "ExcludeWUDriversInQualityUpdate", 1))
            .Labels(L("Inclus"), L("Exclus"))
            .WindowsDefault(TweakDefinition.On)
            .Risk(RiskLevel.Moderate)
            .Warning(L("Vous devrez mettre à jour vos pilotes vous-même (site du fabricant) : un nouveau périphérique peut rester sans pilote adapté."))
            .Build();

        if (TargetVersionTweak() is { } target) yield return target;

        yield return Tweak.Toggle("maintenance.wu.metered", L("Téléchargement sur connexion limitée"),
                L("Autorise le téléchargement automatique des mises à jour sur une connexion définie comme limitée (partage de connexion d'un téléphone, forfait 4G/5G). Par défaut, Windows évite ces téléchargements pour préserver votre forfait. Stratégie officielle « Autoriser le téléchargement automatique des mises à jour sur des connexions limitées »."))
            .In(Cat, GroupUpdates)
            .Keywords(L("connexion limitée, metered, forfait, 4g, partage de connexion, données mobiles"))
            .WhenOn(Reg.LmDword(WuPolicy, "AllowAutoWindowsUpdateDownloadOverMeteredNetwork", 1))
            .WhenOff(Reg.LmDel(WuPolicy, "AllowAutoWindowsUpdateDownloadOverMeteredNetwork"))
            .Labels(L("Autorisé"), L("Évité"))
            .WindowsDefault(TweakDefinition.Off)
            .RecommendWhen(p => p.IsLaptopLike ? TweakDefinition.Off : null)
            .Build();

        // ---------------------------------------------------------------- Assistant de stockage
        yield return Tweak.Toggle("maintenance.storage.enabled", L("Assistant de stockage"),
                L("Libère automatiquement de l'espace : fichiers temporaires des applications et, selon les choix ci-dessous, corbeille et Téléchargements. Il s'exécute à la fréquence choisie, ou quand l'espace disque devient faible. Une stratégie d'organisation peut imposer ce réglage."))
            .In(Cat, GroupStorage)
            .Keywords(L("storage sense, espace disque, nettoyage automatique, libérer de l'espace, stockage"))
            .WhenOn(Reg.CuDword(StoragePolicy, "01", 1))
            .WhenOff(Reg.CuDword(StoragePolicy, "01", 0))
            .WindowsDefault(TweakDefinition.Off)
            .RecommendWhen(p => p.Tier == PerformanceTier.Low || p.SystemDisk is { } d && d.SizeBytes > 0 && d.SizeBytes <= SmallDisk ? TweakDefinition.On : null)
            .Tags("lowend", "family", "office")
            .Build();

        yield return Tweak.Choice("maintenance.storage.cadence", L("Fréquence de l'Assistant de stockage"),
                L("Quand l'Assistant de stockage s'exécute (s'il est activé). « Espace faible » : uniquement lorsque le disque se remplit."))
            .In(Cat, GroupStorage)
            .Keywords(L("storage sense, fréquence, cadence, chaque semaine, chaque mois"))
            .Option("lowspace", L("Espace faible"), Reg.CuDword(StoragePolicy, "2048", 0))
            .Option("daily", L("Chaque jour"), Reg.CuDword(StoragePolicy, "2048", 1))
            .Option("weekly", L("Chaque semaine"), Reg.CuDword(StoragePolicy, "2048", 7))
            .Option("monthly", L("Chaque mois"), Reg.CuDword(StoragePolicy, "2048", 30))
            .WindowsDefault("lowspace")
            .RecommendWhen(p => p.Tier == PerformanceTier.Low || p.SystemDisk is { } d && d.SizeBytes > 0 && d.SizeBytes <= SmallDisk ? "weekly" : null)
            .Tags("lowend")
            .Build();

        yield return Tweak.Toggle("maintenance.storage.tempfiles", L("Nettoyage des fichiers temporaires des applications"),
                L("Lors de son passage, l'Assistant de stockage supprime les fichiers temporaires que les applications n'utilisent plus."))
            .In(Cat, GroupStorage)
            .Keywords(L("storage sense, fichiers temporaires, temp, nettoyage automatique"))
            .WhenOn(Reg.CuDword(StoragePolicy, "04", 1))
            .WhenOff(Reg.CuDword(StoragePolicy, "04", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.On)
            .Tags("lowend")
            .Build();

        yield return Tweak.Choice("maintenance.storage.recyclebin", L("Vidage automatique de la corbeille"),
                L("Supprime définitivement les éléments restés dans la corbeille plus longtemps que la durée choisie (lors du passage de l'Assistant de stockage)."))
            .In(Cat, GroupStorage)
            .Keywords(L("corbeille, recycle bin, storage sense, vider la corbeille"))
            .Option("never", L("Jamais"), Reg.CuDword(StoragePolicy, "08", 0))
            .Option("d1", L("Après 1 jour"), Reg.CuDword(StoragePolicy, "08", 1), Reg.CuDword(StoragePolicy, "256", 1))
            .Option("d14", L("Après 14 jours"), Reg.CuDword(StoragePolicy, "08", 1), Reg.CuDword(StoragePolicy, "256", 14))
            .Option("d30", L("Après 30 jours"), Reg.CuDword(StoragePolicy, "08", 1), Reg.CuDword(StoragePolicy, "256", 30))
            .Option("d60", L("Après 60 jours"), Reg.CuDword(StoragePolicy, "08", 1), Reg.CuDword(StoragePolicy, "256", 60))
            .WindowsDefault("d30")
            .Tags("lowend")
            .Build();

        yield return Tweak.Choice("maintenance.storage.downloads", L("Nettoyage automatique des Téléchargements"),
                L("Supprime les fichiers du dossier Téléchargements qui n'ont pas été ouverts depuis la durée choisie (lors du passage de l'Assistant de stockage)."))
            .In(Cat, GroupStorage)
            .Keywords(L("téléchargements, downloads, storage sense, nettoyage automatique"))
            .Option("never", L("Jamais"), Reg.CuDword(StoragePolicy, "32", 0))
            .Option("d14", L("Après 14 jours"), Reg.CuDword(StoragePolicy, "32", 1), Reg.CuDword(StoragePolicy, "512", 14))
            .Option("d30", L("Après 30 jours"), Reg.CuDword(StoragePolicy, "32", 1), Reg.CuDword(StoragePolicy, "512", 30))
            .Option("d60", L("Après 60 jours"), Reg.CuDword(StoragePolicy, "32", 1), Reg.CuDword(StoragePolicy, "512", 60))
            .WindowsDefault("never")
            .Risk(RiskLevel.Moderate)
            .Warning(L("Les fichiers concernés sont supprimés sans passer par la corbeille : rangez ailleurs ce que vous voulez garder."))
            .Build();
    }

    /// <summary>
    /// « Rester sur la version actuelle » : TargetReleaseVersion/ProductVersion/TargetReleaseVersionInfo (Windows Update pour les entreprises).
    /// La version affichée (ex. 25H2) est lue dans le registre à l'enregistrement : lecture instantanée, identique dans le broker.
    /// </summary>
    private static TweakDefinition? TargetVersionTweak()
    {
        const string cv = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        var display = RegistryAccess.ReadString(RegHive.LocalMachine, cv, "DisplayVersion");
        if (display is null || !DisplayVersionRx().IsMatch(display)) return null;
        var build = int.TryParse(RegistryAccess.ReadString(RegHive.LocalMachine, cv, "CurrentBuildNumber"), out var b) ? b : 0;
        if (build == 0) return null;
        var product = build >= 22000 ? "Windows 11" : "Windows 10";

        return Tweak.Toggle("maintenance.wu.targetversion", L("Verrouillage sur {0} {1}", product, display),
                L("Bloque les mises à niveau de fonctionnalités (la nouvelle version annuelle de Windows) et garde ce PC sur {0} {1}. Les mises à jour de sécurité et les correctifs mensuels continuent d'être installés normalement. Stratégie officielle « Sélectionner la version cible des mises à jour de fonctionnalités » (Windows Update pour les entreprises).", product, display))
            .In(Cat, GroupUpdates)
            .Keywords(L("version cible, TargetReleaseVersion, bloquer mise à niveau, feature update, rester sur, 24h2, 25h2, mise à niveau"))
            .WhenOn(Reg.LmDword(WuPolicy, "TargetReleaseVersion", 1),
                    Reg.LmString(WuPolicy, "ProductVersion", product),
                    Reg.LmString(WuPolicy, "TargetReleaseVersionInfo", display))
            .WhenOff(Reg.LmDel(WuPolicy, "TargetReleaseVersion"),
                     Reg.LmDel(WuPolicy, "ProductVersion"),
                     Reg.LmDel(WuPolicy, "TargetReleaseVersionInfo"))
            .Labels(L("Verrouillé"), L("Libre"))
            .WindowsDefault(TweakDefinition.Off)
            .Risk(RiskLevel.Moderate)
            .Requires(Requires.ProOrHigher)
            .Warning(L("Chaque version n'est prise en charge qu'un temps limité (24 mois pour une version de fin d'année en édition Professionnel, 36 mois en Entreprise/Éducation). Retirez ce verrou avant la fin du support de {0}, sinon ce PC ne recevra plus de correctifs de sécurité.", display))
            .Build();
    }
}
