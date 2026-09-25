using Timonier.Core.Catalog;
using Timonier.Core.Platform;
using Timonier.Core.Search;

namespace Timonier.Modules.Users;

/// <summary>
/// Utilisateurs et contrôle parental : comptes locaux, plages horaires de connexion, restrictions par compte, écran de
/// connexion et verrouillage. Déclarations uniquement : <see cref="Register"/> est aussi appelé dans le broker.
/// </summary>
public sealed class UsersModule : IModule
{
    public const string Category = "users";
    public const string PageId = "users";
    internal const string Glyph = "";

    public void Register(ModuleRegistry r)
    {
        r.AddCategory(new CategoryInfo(Category, "Utilisateurs", Glyph,
            "Comptes locaux, contrôle parental, plages horaires et écran de connexion."));

        r.AddTweaks(UsersTweaks.All());

        r.AddAction(new CreateAccountAction());
        r.AddAction(new DeleteAccountAction());
        r.AddAction(new SetAccountEnabledAction());
        r.AddAction(new SetAccountTypeAction());
        r.AddAction(new ResetPasswordAction());
        r.AddAction(new SetFullNameAction());
        r.AddAction(new SetLogonHoursAction());
        r.AddAction(new GetRestrictionsAction());
        r.AddAction(new SetRestrictionsAction());
        r.AddAction(new LockoutThresholdAction());

        r.AddPage(new PageInfo(PageId, "Utilisateurs", Glyph, NavSection.Control, 30, () => new UsersPage())
        {
            CategoryId = Category,
            Description = "Comptes locaux, contrôle parental (plages horaires, restrictions), écran de connexion.",
            Keywords = ["utilisateurs", "comptes", "compte local", "contrôle parental", "enfant", "famille", "plages horaires",
                        "restrictions", "administrateur", "mot de passe", "écran de connexion", "users", "accounts", "parental control"],
        });

        // Contrôles de santé (lecture seule, hors du thread UI ; une seule énumération partagée).
        r.AddHealthCheck(HealthCheck.Sync("users.builtin-admin", "Compte Administrateur intégré", UsersUi.GlyphAdmin, PageId, UsersHealth.BuiltInAdmin));
        r.AddHealthCheck(HealthCheck.Sync("users.daily-admin", "Compte utilisé au quotidien", UsersUi.GlyphUser, PageId, UsersHealth.DailyAdmin));
        r.AddHealthCheck(HealthCheck.Sync("users.no-password", "Comptes sans mot de passe exigé", UsersUi.GlyphKey, PageId, UsersHealth.NoPassword));

        r.AddQuickAction(new QuickAction("users.quick.family", "Ouvrir Famille Microsoft", Glyph,
            "Contrôle parental Microsoft : temps d'écran, filtres web, achats et rapports d'activité.",
            () => { UsersUi.OpenUri("ms-settings:family-group"); return Task.CompletedTask; })
        {
            Keywords = ["famille", "family safety", "contrôle parental", "enfant", "temps d'écran", "parental control"],
            Order = 60,
        });
        r.AddQuickAction(new QuickAction("users.quick.newaccount", "Créer un compte pour un enfant", UsersUi.GlyphAdd,
            "Compte local standard : il ne peut ni installer de logiciels ni modifier les réglages du PC.",
            () => { AppHost.Navigator.Navigate(PageId, "action:create"); return Task.CompletedTask; })
        {
            Keywords = ["nouveau compte", "ajouter utilisateur", "compte enfant", "créer compte", "add user", "compte standard"],
            Order = 70,
        });

        RegisterSearch(r);

        Synonyms.AddGroup("controle parental", "parental control", "surveillance parentale", "compte enfant", "family safety");
        Synonyms.AddGroup("plages horaires", "horaires de connexion", "heures de connexion", "logon hours", "limite horaire", "couvre feu");
        Synonyms.AddGroup("compte", "utilisateur", "user account", "session");
        Synonyms.AddGroup("compte standard", "compte limite", "utilisateur standard", "sans droits admin");
    }

    private static void RegisterSearch(ModuleRegistry r)
    {
        void Feature(string id, string title, string subtitle, string glyph, string param, string[] keywords, double boost = 0) =>
            r.AddSearchEntry(new SearchEntry
            {
                Id = id, Title = title, Subtitle = subtitle, Glyph = glyph, Keywords = keywords,
                PageId = PageId, PageParameter = param, Boost = boost,
            });

        Feature("users.accounts", "Comptes locaux", "Créer, supprimer, activer, promouvoir ou rétrograder un compte", UsersUi.GlyphUser,
            "section:accounts", ["comptes", "utilisateurs", "ajouter compte", "supprimer compte", "administrateur", "compte standard", "local users"], 0.1);
        Feature("users.newaccount", "Créer un compte utilisateur", "Nouveau compte local standard ou administrateur", UsersUi.GlyphAdd,
            "action:create", ["nouveau compte", "ajouter utilisateur", "créer utilisateur", "add user", "new account", "compte enfant"], 0.1);
        Feature("users.resetpassword", "Réinitialiser le mot de passe d'un compte", "Pour un autre compte local de ce PC", UsersUi.GlyphKey,
            "section:accounts", ["mot de passe oublié", "réinitialiser mot de passe", "reset password", "changer mot de passe"]);
        Feature("users.logonhours", "Plages horaires de connexion", "Heures auxquelles un compte standard peut ouvrir une session", UsersUi.GlyphClock,
            "section:hours", ["plages horaires", "heures de connexion", "temps d'écran", "couvre-feu", "logon hours", "limiter horaires"], 0.1);
        Feature("users.restrictions", "Restrictions d'un compte", "Bloquer Paramètres, Gestionnaire des tâches, invite de commandes, applications…",
            UsersUi.GlyphBlock, "section:restrictions",
            ["restrictions", "bloquer application", "interdire programme", "bloquer panneau de configuration", "disallowrun", "bloquer cmd"], 0.1);
        Feature("users.family", "Contrôle parental Microsoft", "Famille Microsoft, compte standard, DNS familial, accès guidé", UsersUi.GlyphFamily,
            "section:family", ["contrôle parental", "famille", "enfant", "family safety", "parental control"], 0.1);
        Feature("users.lockout", "Verrouillage après mots de passe erronés", "Seuil de verrouillage des comptes (force brute)", UsersUi.GlyphLock,
            "section:signin", ["verrouillage compte", "tentatives mot de passe", "lockout", "force brute", "seuil"]);

        void Uri(string id, string title, string subtitle, string uri, string[] keywords) =>
            r.AddSearchEntry(new SearchEntry
            {
                Id = id, Title = title, Subtitle = subtitle, Glyph = Glyph, Keywords = keywords, Kind = SearchEntryKind.WindowsSetting,
                Execute = () => UsersUi.OpenUri(uri),
            });

        Uri("users.win.otherusers", "Autres utilisateurs", "Paramètres › Comptes › Autres utilisateurs", "ms-settings:otherusers",
            ["autres utilisateurs", "ajouter un compte", "other users"]);
        Uri("users.win.family", "Famille", "Paramètres › Comptes › Famille (contrôle parental Microsoft)", "ms-settings:family-group",
            ["famille", "family", "contrôle parental"]);
        Uri("users.win.signin", "Options de connexion", "Code PIN, Windows Hello, mot de passe, clé de sécurité", "ms-settings:signinoptions",
            ["options de connexion", "code pin", "windows hello", "empreinte", "reconnaissance faciale", "sign-in options"]);
        Uri("users.win.yourinfo", "Vos informations", "Paramètres › Comptes › Vos informations", "ms-settings:yourinfo",
            ["mon compte", "photo de profil", "your info", "compte microsoft"]);

        void Tool(string id, string title, string subtitle, string[] keywords, Action run) =>
            r.AddSearchEntry(new SearchEntry
            {
                Id = id, Title = title, Subtitle = subtitle, Glyph = Glyph, Keywords = keywords, Kind = SearchEntryKind.Tool, Execute = run,
            });

        Tool("users.tool.netplwiz", "Comptes d'utilisateurs (netplwiz)", "Outil classique de gestion des comptes et des groupes",
            ["netplwiz", "control userpasswords2", "comptes utilisateurs"], () => UsersUi.Launch(SystemTool.Netplwiz));
        Tool("users.tool.lusrmgr", "Utilisateurs et groupes locaux (lusrmgr.msc)", "Console de gestion (éditions Professionnel et supérieures)",
            ["lusrmgr", "groupes locaux", "local users and groups"],
            () => UsersUi.Launch(SystemTool.Mmc, Path.Combine(Environment.SystemDirectory, "lusrmgr.msc")));
        Tool("users.tool.profiles", "Profils des utilisateurs", "Supprimer le dossier de profil d'un compte supprimé",
            ["profils utilisateurs", "supprimer profil", "user profiles", "dossier utilisateur"],
            () => UsersUi.Launch(SystemTool.Rundll32, "sysdm.cpl,EditUserProfiles"));
    }
}

/// <summary>Contrôles de santé : une énumération partagée (mise en cache 30 s) pour les trois contrôles.</summary>
internal static class UsersHealth
{
    private static readonly Lock Gate = new();
    private static (DateTime At, List<LocalAccount> Accounts)? _cache;

    private static List<LocalAccount> Accounts()
    {
        lock (Gate)
        {
            if (_cache is { } c && DateTime.UtcNow - c.At < TimeSpan.FromSeconds(30)) return c.Accounts;
            var list = LocalAccounts.Enumerate();
            _cache = (DateTime.UtcNow, list);
            return list;
        }
    }

    public static void Invalidate()
    {
        lock (Gate) _cache = null;
    }

    private static HealthResult Safe(Func<List<LocalAccount>, HealthResult> check)
    {
        try { return check(Accounts()); }
        catch (Exception ex)
        {
            Log.Warn("Users", "contrôle de santé : " + ex.Message);
            return new HealthResult(HealthStatus.Unknown, "Comptes locaux illisibles", ex.Message);
        }
    }

    public static HealthResult BuiltInAdmin() => Safe(all =>
    {
        var admin = all.FirstOrDefault(a => a.IsBuiltInAdministrator);
        if (admin is null || !admin.Enabled)
            return new HealthResult(HealthStatus.Good, "Compte Administrateur intégré désactivé");
        return new HealthResult(HealthStatus.Warning, $"Le compte « {admin.Name} » intégré est activé",
            "Ce compte a tous les droits sans invite UAC et son nom est connu d'avance : c'est une cible classique. " +
            "Désactivez-le depuis la page Utilisateurs après avoir vérifié qu'un autre compte administrateur fonctionne.");
    });

    public static HealthResult DailyAdmin() => Safe(all =>
    {
        var me = all.FirstOrDefault(a => a.IsCurrent);
        if (me is null) return new HealthResult(HealthStatus.Info, "Compte de domaine ou Microsoft Entra", "Votre compte n'est pas un compte local de ce PC.");
        if (!me.IsAdmin) return new HealthResult(HealthStatus.Good, "Vous utilisez un compte standard");
        return new HealthResult(HealthStatus.Info, "Vous utilisez un compte administrateur au quotidien",
            "Un compte standard pour l'usage courant limite les dégâts d'un logiciel malveillant : l'UAC demande alors le mot de passe " +
            "d'un administrateur. Gardez un compte administrateur distinct pour les installations.");
    });

    public static HealthResult NoPassword() => Safe(all =>
    {
        var flagged = all.Where(a => a.Enabled && a.PasswordNotRequired && a.MicrosoftAccount is null && !a.IsSystemAccount).ToList();
        if (flagged.Count == 0) return new HealthResult(HealthStatus.Good, "Tous les comptes actifs exigent un mot de passe");
        var names = string.Join(", ", flagged.Select(a => a.Name));
        return new HealthResult(HealthStatus.Info,
            flagged.Count == 1 ? $"1 compte accepte un mot de passe vide : {names}" : $"{flagged.Count} comptes acceptent un mot de passe vide : {names}",
            "Windows autorise un mot de passe vide pour ces comptes. Timonier ne peut pas vérifier sans tentative de connexion s'il est " +
            "réellement vide ; si c'est le cas, n'importe qui peut ouvrir la session en local. Définissez un mot de passe ou un code PIN.");
    });
}
