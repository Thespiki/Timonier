using PcPilot.Core.Model;
using PcPilot.Core.Platform;

namespace PcPilot.Modules.Startup;

/// <summary>Réglages liés à l'ouverture de session (compte courant, sans élévation).</summary>
internal static class StartupTweaks
{
    private const string Group = "Ouverture de session";
    private const string StartupToast = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\Windows.SystemToast.StartupApp";
    private const string Winlogon = @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon";

    public static IEnumerable<TweakDefinition> All()
    {
        yield return Tweak.Toggle("startup.notify.newapp", "Notification d'ajout au démarrage",
                "Windows affiche une notification lorsqu'une application s'inscrit pour se lancer à l'ouverture de session. "
                + "Pratique pour repérer les logiciels qui s'ajoutent au démarrage sans vous le demander. "
                + "Correspond à « Notification d'application de démarrage » dans Paramètres › Système › Notifications.")
            .In(StartupModule.Category, Group)
            .Keywords("notification", "nouvelle application", "startup app notification", "alerte démarrage", "surveiller démarrage")
            .Tags("lowend", "office")
            .Requires(Requires.Windows11_22H2)
            .WhenOn(Reg.CuDword(StartupToast, "Enabled", 1))
            .WhenOff(Reg.CuDword(StartupToast, "Enabled", 0))
            .Detect(() => RegistryAccess.ReadDword(RegHive.CurrentUser, StartupToast, "Enabled") == 1 ? TweakDefinition.On : TweakDefinition.Off)
            .WindowsDefault(TweakDefinition.Off)
            .Recommend(TweakDefinition.On)
            .Warning("La notification n'apparaît que si les notifications de Windows sont activées.")
            .Build();

        yield return Tweak.Toggle("startup.restartapps", "Relancer mes applications à la reconnexion",
                "À la fermeture de session ou au redémarrage, Windows mémorise les applications ouvertes qui le permettent "
                + "et les relance à la connexion suivante. Pratique pour reprendre son travail, mais cela alourdit "
                + "l'ouverture de session. Correspond à « Enregistrer automatiquement mes applications redémarrables » "
                + "dans Paramètres › Comptes › Options de connexion.")
            .In(StartupModule.Category, Group)
            .Keywords("restart apps", "rouvrir applications", "reprendre applications", "restaurer session", "applications redémarrables")
            .Tags("lowend", "office")
            .WhenOn(Reg.CuDword(Winlogon, "RestartApps", 1))
            .WhenOff(Reg.CuDword(Winlogon, "RestartApps", 0))
            .Detect(() => RegistryAccess.ReadDword(RegHive.CurrentUser, Winlogon, "RestartApps") == 1 ? TweakDefinition.On : TweakDefinition.Off)
            .WindowsDefault(TweakDefinition.Off)
            .RecommendWhen(p => p.Tier == PerformanceTier.Low || p.RamGb is > 0 and < 6 ? TweakDefinition.Off : null)
            .Build();
    }
}
