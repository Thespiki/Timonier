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
    public static string GroupLogon => L("Sign-in screen");
    public static string GroupAccounts => L("Accounts and lockout");

    /// <summary>HKLM\…\Policies\System (stratégies de sécurité locales « Ouverture de session interactive »).</summary>
    private const string PolSys = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    /// <summary>HKLM\SOFTWARE\Policies\Microsoft\Windows\System (modèles d'administration Système › Ouverture de session).</summary>
    private const string WinSystem = @"SOFTWARE\Policies\Microsoft\Windows\System";

    public static IEnumerable<TweakDefinition> All()
    {
        yield return Tweak.Toggle("users.logon.lastuser", L("Last user's name on the sign-in screen"),
                L("Shows the name (and picture) of the last signed-in account on the sign-in screen. When hidden, you have to type the account name as well as the password: someone who finds the PC doesn't know which accounts exist."))
            .In(C, GroupLogon)
            .Keywords(L("last user, username, sign-in screen, dontdisplaylastusername, logon screen, login screen, hide account"))
            .Tags("security", "office")
            .Labels(L("Shown"), L("Hidden"))
            .Warning(L("You'll have to type the exact account name at every sign-in; signing in with a PIN or Windows Hello may no longer be offered directly."))
            .WhenOn(Reg.LmDword(PolSys, "DontDisplayLastUserName", 0))
            .WhenOff(Reg.LmDword(PolSys, "DontDisplayLastUserName", 1))
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Choice("users.logon.lockedinfo", L("Info shown when the session is locked"),
                L("What the lock screen shows about the signed-in account: by default, its name and sometimes its email address."))
            .In(C, GroupLogon)
            .Keywords(L("lock screen, locked session, display name, dontdisplaylockeduserid, email address"))
            .Tags("security", "office")
            .Option("default", L("Default (name and address)"), Reg.LmDel(PolSys, "DontDisplayLockedUserId"))
            .Option("name", L("Display name only"), Reg.LmDword(PolSys, "DontDisplayLockedUserId", 2))
            .Option("none", L("No information"), Reg.LmDword(PolSys, "DontDisplayLockedUserId", 3))
            .WindowsDefault("default")
            .Build();

        yield return Tweak.Toggle("users.logon.accountdetails", L("Account email address on the sign-in screen"),
                L("For a Microsoft account, Windows can show the email address under the name on the sign-in screen. When blocked, the address no longer appears, which avoids exposing it on a shared or public PC."))
            .In(C, GroupLogon)
            .Keywords(L("email address, email, sign-in screen, microsoft account, account details, privacy"))
            .Tags("security", "office", "kiosk")
            .Labels(LC("feminine", "Shown"), LC("feminine", "Hidden"))
            .Requires(new Requirement { MinBuild = 14393 })
            .WhenOn(Reg.LmDel(WinSystem, "BlockUserFromShowingAccountDetailsOnSignin"))
            .WhenOff(Reg.LmDword(WinSystem, "BlockUserFromShowingAccountDetailsOnSignin", 1))
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("users.logon.fastswitch", L("Fast user switching"),
                L("Lets you sign in to another account without closing the current session (Start menu › account icon, lock screen). When off, everyone must sign out before someone else signs in: useful on a low-powered family PC, where several open sessions use up memory."))
            .In(C, GroupLogon)
            .Keywords(L("switch user, fast user switching, hidefastuserswitching, multiple sessions"))
            .Tags("family", "kiosk", "lowend")
            .Labels(L("Allowed"), L("Hidden"))
            .Warning(L("The entry points are hidden, but a session that's already open isn't closed."))
            .WhenOn(Reg.LmDel(PolSys, "HideFastUserSwitching"))
            .WhenOff(Reg.LmDword(PolSys, "HideFastUserSwitching", 1))
            .WindowsDefault(On)
            .RecommendWhen(p => p.HardwareLoaded && p.RamGb is > 0 and < 6 ? Off : null)
            .Build();

        yield return Tweak.Toggle("users.logon.firstanimation", L("First sign-in animation"),
                L("The “Hi” / “We're getting everything ready for you” screens shown the first time a new account signs in. When off, the profile is still set up, but behind a plainer screen."))
            .In(C, GroupLogon)
            .Keywords(L("first sign-in, first logon animation, animation, hi, new account"))
            .Tags("lowend", "kiosk")
            .WhenOn(Reg.LmDel(PolSys, "EnableFirstLogonAnimation"))
            .WhenOff(Reg.LmDword(PolSys, "EnableFirstLogonAnimation", 0))
            .WindowsDefault(On)
            .RecommendWhen(p => p.Tier == PerformanceTier.Low ? Off : null)
            .Build();

        yield return Tweak.Toggle("users.logon.arso", L("Automatic sign-in after an update"),
                L("After a restart triggered by Windows Update, Windows uses your sign-in info to finish setting up your session, then locks it immediately so your apps are ready when you're back."))
            .In(C, GroupLogon)
            .Keywords(L("arso, automatic sign-in, after update, sign-in info, restart sign on, finish setup"))
            .Tags("security")
            .Labels(LC("feminine", "On"), L("Disabled"))
            .WhenOn(Reg.LmDel(PolSys, "DisableAutomaticRestartSignOn"))
            .WhenOff(Reg.LmDword(PolSys, "DisableAutomaticRestartSignOn", 1))
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("users.logon.domainlocalusers", L("Local accounts on the sign-in screen"),
                L("On a PC joined to an Active Directory domain, Windows doesn't show the list of local accounts at sign-in. This policy shows it, for example for a backup or demo local account."))
            .In(C, GroupLogon)
            .Keywords(L("local accounts, domain, enumerate local users, user list, active directory"))
            .Tags("office")
            .Requires(Requires.When(p => p.IsDomainJoined, L("Only applies to PCs joined to an Active Directory domain.")))
            .Labels(LC("plural", "Shown"), LC("plural", "Hidden"))
            .WhenOn(Reg.LmDword(WinSystem, "EnumerateLocalUsers", 1))
            .WhenOff(Reg.LmDel(WinSystem, "EnumerateLocalUsers"))
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Toggle("users.accounts.addmsa", L("Adding Microsoft accounts"),
                L("Allows adding Microsoft accounts on this PC (and converting a local account to a Microsoft account). When blocked, only local accounts can be created; existing Microsoft accounts keep working."))
            .In(C, GroupAccounts)
            .Keywords(L("microsoft account, new account, add account, noconnecteduser, block microsoft accounts, local account"))
            .Tags("kiosk", "office")
            .Labels(L("Allowed"), L("Blocked"))
            .Warning(L("Microsoft parental controls (Family) require the child to have a Microsoft account: don't block adding accounts if you plan to use them."))
            .WhenOn(Reg.LmDel(PolSys, "NoConnectedUser"))
            .WhenOff(Reg.LmDword(PolSys, "NoConnectedUser", 1))
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("users.accounts.securityquestions", L("Security questions for local accounts"),
                L("Asks for three security questions when a local account is created, so its password can be reset from the sign-in screen. Handy, but the answers (city of birth, pet's name…) are often easy for someone close to you to guess."))
            .In(C, GroupAccounts)
            .Keywords(L("security questions, forgot password, reset password, password reset"))
            .Tags("security")
            .Labels(L("Used"), LC("feminine plural", "Disabled"))
            .Requires(new Requirement { MinBuild = 18362 })
            .Warning(L("Without security questions, a forgotten local password can only be reset by another administrator (or with a password reset disk)."))
            .WhenOn(Reg.LmDel(WinSystem, "NoLocalPasswordResetQuestions"))
            .WhenOff(Reg.LmDword(WinSystem, "NoLocalPasswordResetQuestions", 1))
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Choice("users.accounts.inactivity", L("Automatic lock after inactivity"),
                L("Locks the session when the keyboard and mouse haven't been used for the chosen time, even if the screen saver is off (“Machine inactivity limit” policy). Protects a PC left on. Depending on the Windows version, a restart may be needed for it to take effect."))
            .In(C, GroupAccounts)
            .Keywords(L("automatic lock, auto lock, inactivity, idle, lock session, inactivitytimeoutsecs, screen timeout"))
            .Tags("security", "office", "family")
            .Option("default", L("Not set (Windows)"), Reg.LmDel(PolSys, "InactivityTimeoutSecs"))
            .Option("5", L("After 5 minutes"), Reg.LmDword(PolSys, "InactivityTimeoutSecs", 300))
            .Option("10", L("After 10 minutes"), Reg.LmDword(PolSys, "InactivityTimeoutSecs", 600))
            .Option("15", L("After 15 minutes"), Reg.LmDword(PolSys, "InactivityTimeoutSecs", 900))
            .Option("30", L("After 30 minutes"), Reg.LmDword(PolSys, "InactivityTimeoutSecs", 1800))
            .WindowsDefault("default")
            .Build();
    }
}
