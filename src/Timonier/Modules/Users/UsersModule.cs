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
        r.AddCategory(new CategoryInfo(Category, L("Users"), Glyph,
            L("Local accounts, parental controls, sign-in hours and sign-in screen.")));

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

        r.AddPage(new PageInfo(PageId, L("Users"), Glyph, NavSection.Control, 30, () => new UsersPage())
        {
            CategoryId = Category,
            Description = L("Local accounts, parental controls (sign-in hours, restrictions), sign-in screen."),
            Keywords = [L("users, accounts, local account, parental controls, child, family, sign-in hours, restrictions, administrator, password, sign-in screen, login screen")],
        });

        // Contrôles de santé (lecture seule, hors du thread UI ; une seule énumération partagée).
        r.AddHealthCheck(HealthCheck.Sync("users.builtin-admin", L("Built-in Administrator account"), UsersUi.GlyphAdmin, PageId, UsersHealth.BuiltInAdmin));
        r.AddHealthCheck(HealthCheck.Sync("users.daily-admin", L("Account used day to day"), UsersUi.GlyphUser, PageId, UsersHealth.DailyAdmin));
        r.AddHealthCheck(HealthCheck.Sync("users.no-password", L("Accounts with no password required"), UsersUi.GlyphKey, PageId, UsersHealth.NoPassword));

        r.AddQuickAction(new QuickAction("users.quick.family", L("Open Microsoft Family"), Glyph,
            L("Microsoft parental controls: screen time, web filters, purchases and activity reports."),
            () => { UsersUi.OpenUri("ms-settings:family-group"); return Task.CompletedTask; })
        {
            Keywords = [L("family, family safety, parental controls, child, kids, screen time")],
            Order = 60,
        });
        r.AddQuickAction(new QuickAction("users.quick.newaccount", L("Create an account for a child"), UsersUi.GlyphAdd,
            L("Standard local account: it can't install software or change PC settings."),
            () => { AppHost.Navigator.Navigate(PageId, "action:create"); return Task.CompletedTask; })
        {
            Keywords = [L("new account, add user, child account, create account, standard account, kid account")],
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

        Feature("users.accounts", L("Local accounts"), L("Create, delete, enable, promote or demote an account"), UsersUi.GlyphUser,
            "section:accounts", [L("accounts, users, add account, delete account, remove account, administrator, standard account, local users")], 0.1);
        Feature("users.newaccount", L("Create a user account"), L("New standard or administrator local account"), UsersUi.GlyphAdd,
            "action:create", [L("new account, add user, create user, new user, child account")], 0.1);
        Feature("users.resetpassword", L("Reset an account's password"), L("For another local account on this PC"), UsersUi.GlyphKey,
            "section:accounts", [L("forgot password, reset password, password reset, change password")]);
        Feature("users.logonhours", L("Sign-in hours"), L("Hours when a standard account can sign in"), UsersUi.GlyphClock,
            "section:hours", [L("sign-in hours, logon hours, screen time, curfew, limit hours, time limits")], 0.1);
        Feature("users.restrictions", LC("feature name", "Account restrictions"), L("Block Settings, Task Manager, Command Prompt, apps…"),
            UsersUi.GlyphBlock, "section:restrictions",
            [L("restrictions, block app, block program, prevent program, block control panel, disallowrun, block cmd")], 0.1);
        Feature("users.family", L("Microsoft parental controls"), L("Microsoft Family, standard account, family DNS, guided access"), UsersUi.GlyphFamily,
            "section:family", [L("parental controls, family, child, kids, family safety")], 0.1);
        Feature("users.lockout", LC("feature name", "Lockout after wrong passwords"), L("Account lockout threshold (brute force)"), UsersUi.GlyphLock,
            "section:signin", [L("account lockout, password attempts, lockout, brute force, threshold, failed sign-ins")]);

        void Uri(string id, string title, string subtitle, string uri, string[] keywords) =>
            r.AddSearchEntry(new SearchEntry
            {
                Id = id, Title = title, Subtitle = subtitle, Glyph = Glyph, Keywords = keywords, Kind = SearchEntryKind.WindowsSetting,
                Execute = () => UsersUi.OpenUri(uri),
            });

        Uri("users.win.otherusers", L("Other users"), L("Settings › Accounts › Other users"), "ms-settings:otherusers",
            [L("other users, add account, add user, new user")]);
        Uri("users.win.family", L("Family"), L("Settings › Accounts › Family (Microsoft parental controls)"), "ms-settings:family-group",
            [L("family, parental controls, family safety, kids")]);
        Uri("users.win.signin", L("Sign-in options"), L("PIN, Windows Hello, password, security key"), "ms-settings:signinoptions",
            [L("sign-in options, pin, windows hello, fingerprint, facial recognition, face recognition")]);
        Uri("users.win.yourinfo", L("Your info"), L("Settings › Accounts › Your info"), "ms-settings:yourinfo",
            [L("my account, profile picture, your info, microsoft account")]);

        void Tool(string id, string title, string subtitle, string[] keywords, Action run) =>
            r.AddSearchEntry(new SearchEntry
            {
                Id = id, Title = title, Subtitle = subtitle, Glyph = Glyph, Keywords = keywords, Kind = SearchEntryKind.Tool, Execute = run,
            });

        Tool("users.tool.netplwiz", L("User Accounts (netplwiz)"), L("Classic tool for managing accounts and groups"),
            ["netplwiz", "control userpasswords2", L("user accounts")], () => UsersUi.Launch(SystemTool.Netplwiz));
        Tool("users.tool.lusrmgr", L("Local Users and Groups (lusrmgr.msc)"), L("Management console (Pro editions and higher)"),
            ["lusrmgr", L("local groups, local users and groups")],
            () => UsersUi.Launch(SystemTool.Mmc, Path.Combine(Environment.SystemDirectory, "lusrmgr.msc")));
        Tool("users.tool.profiles", L("User Profiles"), L("Delete the profile folder of a deleted account"),
            [L("user profiles, delete profile, user folder")],
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
            return new HealthResult(HealthStatus.Unknown, L("Local accounts unreadable"), ex.Message);
        }
    }

    public static HealthResult BuiltInAdmin() => Safe(all =>
    {
        var admin = all.FirstOrDefault(a => a.IsBuiltInAdministrator);
        if (admin is null || !admin.Enabled)
            return new HealthResult(HealthStatus.Good, L("Built-in Administrator account disabled"));
        return new HealthResult(HealthStatus.Warning, L("The built-in “{0}” account is enabled", admin.Name),
            L("This account has full rights without a UAC prompt and its name is known in advance: it's a classic target. Disable it from the Users page after checking that another administrator account works."));
    });

    public static HealthResult DailyAdmin() => Safe(all =>
    {
        var me = all.FirstOrDefault(a => a.IsCurrent);
        if (me is null) return new HealthResult(HealthStatus.Info, L("Domain or Microsoft Entra account"), L("Your account isn't a local account on this PC."));
        if (!me.IsAdmin) return new HealthResult(HealthStatus.Good, L("You're using a standard account"));
        return new HealthResult(HealthStatus.Info, L("You're using an administrator account every day"),
            L("A standard account for everyday use limits the damage malware can do: UAC then asks for an administrator's password. Keep a separate administrator account for installations."));
    });

    public static HealthResult NoPassword() => Safe(all =>
    {
        var flagged = all.Where(a => a.Enabled && a.PasswordNotRequired && a.MicrosoftAccount is null && !a.IsSystemAccount).ToList();
        if (flagged.Count == 0) return new HealthResult(HealthStatus.Good, L("All active accounts require a password"));
        var names = string.Join(", ", flagged.Select(a => a.Name));
        return new HealthResult(HealthStatus.Info,
            LP(flagged.Count, "{0} account accepts a blank password: {1}", "{0} accounts accept a blank password: {1}", names),
            L("Windows allows a blank password for these accounts. Timonier can't check whether it's actually blank without trying to sign in; if it is, anyone can sign in locally. Set a password or a PIN."));
    });
}
