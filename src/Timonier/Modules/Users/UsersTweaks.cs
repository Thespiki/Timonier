using Timonier.Core.Model;
using Timonier.Core.Platform;
using static Timonier.Core.Model.TweakDefinition;

namespace Timonier.Modules.Users;

/// <summary>
/// Réglages déclaratifs de l'écran de connexion et des comptes. Toutes les valeurs sont des stratégies documentées
/// (Microsoft Learn : « Security policy settings » et modèles d'administration Logon.admx / CredUI.admx).
/// </summary>
internal static class UsersTweaks
{
    private const string C = UsersModule.Category;
    public const string GroupLogon = "Écran de connexion";
    public const string GroupAccounts = "Comptes et verrouillage";

    /// <summary>HKLM\…\Policies\System (stratégies de sécurité locales « Ouverture de session interactive »).</summary>
    private const string PolSys = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    /// <summary>HKLM\SOFTWARE\Policies\Microsoft\Windows\System (modèles d'administration Système › Ouverture de session).</summary>
    private const string WinSystem = @"SOFTWARE\Policies\Microsoft\Windows\System";

    public static IEnumerable<TweakDefinition> All()
    {
        yield return Tweak.Toggle("users.logon.lastuser", "Nom du dernier utilisateur à l'écran de connexion",
                "Affiche le nom (et l'image) du dernier compte connecté sur l'écran de connexion. Masqué, il faut taper le nom du " +
                "compte en plus du mot de passe : une personne qui trouve le PC ne sait pas quels comptes existent.")
            .In(C, GroupLogon)
            .Keywords("dernier utilisateur", "nom d'utilisateur", "écran de connexion", "dontdisplaylastusername", "last user", "logon screen", "masquer compte")
            .Tags("security", "office")
            .Labels("Affiché", "Masqué")
            .Warning("Il faudra saisir le nom exact du compte à chaque connexion ; la connexion par code PIN ou Windows Hello " +
                     "peut ne plus être proposée directement.")
            .WhenOn(Reg.LmDword(PolSys, "DontDisplayLastUserName", 0))
            .WhenOff(Reg.LmDword(PolSys, "DontDisplayLastUserName", 1))
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Choice("users.logon.lockedinfo", "Informations affichées quand la session est verrouillée",
                "Ce que l'écran de verrouillage montre du compte connecté : par défaut, son nom et parfois son adresse e-mail.")
            .In(C, GroupLogon)
            .Keywords("écran de verrouillage", "session verrouillée", "nom affiché", "dontdisplaylockeduserid", "lock screen", "adresse e-mail")
            .Tags("security", "office")
            .Option("default", "Par défaut (nom et adresse)", Reg.LmDel(PolSys, "DontDisplayLockedUserId"))
            .Option("name", "Nom affiché uniquement", Reg.LmDword(PolSys, "DontDisplayLockedUserId", 2))
            .Option("none", "Aucune information", Reg.LmDword(PolSys, "DontDisplayLockedUserId", 3))
            .WindowsDefault("default")
            .Build();

        yield return Tweak.Toggle("users.logon.accountdetails", "Adresse e-mail du compte à l'écran de connexion",
                "Pour un compte Microsoft, Windows peut afficher l'adresse e-mail sous le nom sur l'écran de connexion. " +
                "Bloquée, l'adresse n'apparaît plus, ce qui évite de l'exposer sur un PC partagé ou en public.")
            .In(C, GroupLogon)
            .Keywords("adresse e-mail", "email", "écran de connexion", "compte microsoft", "account details", "vie privée")
            .Tags("security", "office", "kiosk")
            .Labels("Affichée", "Masquée")
            .Requires(new Requirement { MinBuild = 14393 })
            .WhenOn(Reg.LmDel(WinSystem, "BlockUserFromShowingAccountDetailsOnSignin"))
            .WhenOff(Reg.LmDword(WinSystem, "BlockUserFromShowingAccountDetailsOnSignin", 1))
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("users.logon.fastswitch", "Changement rapide d'utilisateur",
                "Permet d'ouvrir un autre compte sans fermer la session en cours (menu Démarrer › icône du compte, écran de " +
                "verrouillage). Désactivé, chacun doit se déconnecter avant que quelqu'un d'autre se connecte : utile sur un PC " +
                "familial peu puissant, où plusieurs sessions ouvertes consomment de la mémoire.")
            .In(C, GroupLogon)
            .Keywords("changer d'utilisateur", "fast user switching", "hidefastuserswitching", "plusieurs sessions", "switch user")
            .Tags("family", "kiosk", "lowend")
            .Labels("Autorisé", "Masqué")
            .Warning("Les points d'entrée sont masqués, mais une session déjà ouverte n'est pas fermée.")
            .WhenOn(Reg.LmDel(PolSys, "HideFastUserSwitching"))
            .WhenOff(Reg.LmDword(PolSys, "HideFastUserSwitching", 1))
            .WindowsDefault(On)
            .RecommendWhen(p => p.HardwareLoaded && p.RamGb is > 0 and < 6 ? Off : null)
            .Build();

        yield return Tweak.Toggle("users.logon.firstanimation", "Animation de première connexion",
                "Écrans « Bonjour » / « Nous préparons tout pour vous » affichés à la première connexion d'un nouveau compte. " +
                "Désactivée, la préparation du profil a lieu quand même, mais derrière un écran plus sobre.")
            .In(C, GroupLogon)
            .Keywords("première connexion", "animation", "bonjour", "nouveau compte", "first logon animation", "first sign-in")
            .Tags("lowend", "kiosk")
            .WhenOn(Reg.LmDel(PolSys, "EnableFirstLogonAnimation"))
            .WhenOff(Reg.LmDword(PolSys, "EnableFirstLogonAnimation", 0))
            .WindowsDefault(On)
            .RecommendWhen(p => p.Tier == PerformanceTier.Low ? Off : null)
            .Build();

        yield return Tweak.Toggle("users.logon.arso", "Reconnexion automatique après une mise à jour",
                "Après un redémarrage lancé par Windows Update, Windows utilise vos informations de connexion pour terminer la " +
                "configuration de votre session puis la verrouille aussitôt, afin que vos applications soient prêtes au retour.")
            .In(C, GroupLogon)
            .Keywords("arso", "reconnexion automatique", "après mise à jour", "sign-in info", "restart sign on", "terminer la configuration")
            .Tags("security")
            .Labels("Activée", "Désactivée")
            .WhenOn(Reg.LmDel(PolSys, "DisableAutomaticRestartSignOn"))
            .WhenOff(Reg.LmDword(PolSys, "DisableAutomaticRestartSignOn", 1))
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("users.logon.domainlocalusers", "Comptes locaux sur l'écran de connexion",
                "Sur un PC joint à un domaine Active Directory, Windows n'affiche pas la liste des comptes locaux à la connexion. " +
                "Cette stratégie l'affiche, par exemple pour un compte local de secours ou de démonstration.")
            .In(C, GroupLogon)
            .Keywords("comptes locaux", "domaine", "enumerate local users", "liste des utilisateurs", "active directory")
            .Tags("office")
            .Requires(Requires.When(p => p.IsDomainJoined, "Concerne uniquement les PC joints à un domaine Active Directory."))
            .Labels("Affichés", "Masqués")
            .WhenOn(Reg.LmDword(WinSystem, "EnumerateLocalUsers", 1))
            .WhenOff(Reg.LmDel(WinSystem, "EnumerateLocalUsers"))
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Toggle("users.accounts.addmsa", "Ajout de comptes Microsoft",
                "Autorise l'ajout de comptes Microsoft sur ce PC (et la conversion d'un compte local en compte Microsoft). " +
                "Bloqué, seuls des comptes locaux peuvent être créés ; les comptes Microsoft déjà présents continuent de fonctionner.")
            .In(C, GroupAccounts)
            .Keywords("compte microsoft", "nouveau compte", "noconnecteduser", "microsoft account", "bloquer comptes microsoft", "compte local")
            .Tags("kiosk", "office")
            .Labels("Autorisé", "Bloqué")
            .Warning("Le contrôle parental Microsoft (Famille) exige que l'enfant ait un compte Microsoft : ne bloquez pas l'ajout si vous comptez l'utiliser.")
            .WhenOn(Reg.LmDel(PolSys, "NoConnectedUser"))
            .WhenOff(Reg.LmDword(PolSys, "NoConnectedUser", 1))
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("users.accounts.securityquestions", "Questions de sécurité des comptes locaux",
                "Demande trois questions de sécurité à la création d'un compte local, pour pouvoir réinitialiser son mot de passe " +
                "depuis l'écran de connexion. Pratique, mais les réponses (ville de naissance, nom d'un animal…) sont souvent " +
                "faciles à deviner pour un proche.")
            .In(C, GroupAccounts)
            .Keywords("questions de sécurité", "mot de passe oublié", "security questions", "réinitialiser mot de passe", "password reset")
            .Tags("security")
            .Labels("Utilisées", "Désactivées")
            .Requires(new Requirement { MinBuild = 18362 })
            .Warning("Sans questions de sécurité, un mot de passe local oublié ne peut être réinitialisé que par un autre administrateur " +
                     "(ou avec un disque de réinitialisation).")
            .WhenOn(Reg.LmDel(WinSystem, "NoLocalPasswordResetQuestions"))
            .WhenOff(Reg.LmDword(WinSystem, "NoLocalPasswordResetQuestions", 1))
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Choice("users.accounts.inactivity", "Verrouillage automatique après inactivité",
                "Verrouille la session quand le clavier et la souris ne sont plus utilisés pendant la durée choisie, même si " +
                "l'économiseur d'écran est désactivé (stratégie « Limite d'inactivité de l'ordinateur »). Protège un PC laissé allumé. Selon les versions de Windows, un redémarrage peut être nécessaire pour la prise en compte.")
            .In(C, GroupAccounts)
            .Keywords("verrouillage automatique", "inactivité", "verrouiller session", "inactivitytimeoutsecs", "auto lock", "mise en veille écran")
            .Tags("security", "office", "family")
            .Option("default", "Non défini (Windows)", Reg.LmDel(PolSys, "InactivityTimeoutSecs"))
            .Option("5", "Après 5 minutes", Reg.LmDword(PolSys, "InactivityTimeoutSecs", 300))
            .Option("10", "Après 10 minutes", Reg.LmDword(PolSys, "InactivityTimeoutSecs", 600))
            .Option("15", "Après 15 minutes", Reg.LmDword(PolSys, "InactivityTimeoutSecs", 900))
            .Option("30", "Après 30 minutes", Reg.LmDword(PolSys, "InactivityTimeoutSecs", 1800))
            .WindowsDefault("default")
            .Build();
    }
}
