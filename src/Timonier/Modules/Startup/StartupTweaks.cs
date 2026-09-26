using Timonier.Core.Model;
using Timonier.Core.Platform;

namespace Timonier.Modules.Startup;

/// <summary>Réglages liés à l'ouverture de session (compte courant, sans élévation).</summary>
internal static class StartupTweaks
{
    private static string Group => L("Sign-in");
    private const string StartupToast = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\Windows.SystemToast.StartupApp";
    private const string Winlogon = @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon";

    public static IEnumerable<TweakDefinition> All()
    {
        yield return Tweak.Toggle("startup.notify.newapp", L("Startup app notification"),
                L("Windows shows a notification when an app registers itself to run at sign-in. Handy for spotting software that adds itself to startup without asking you. Matches “Startup app notification” in Settings › System › Notifications."))
            .In(StartupModule.Category, Group)
            .Keywords(L("notification, new app, startup app notification, startup alert, monitor startup"))
            .Tags("lowend", "office")
            .Requires(Requires.Windows11_22H2)
            .WhenOn(Reg.CuDword(StartupToast, "Enabled", 1))
            .WhenOff(Reg.CuDword(StartupToast, "Enabled", 0))
            .Detect(() => RegistryAccess.ReadDword(RegHive.CurrentUser, StartupToast, "Enabled") == 1 ? TweakDefinition.On : TweakDefinition.Off)
            .WindowsDefault(TweakDefinition.Off)
            .Recommend(TweakDefinition.On)
            .Warning(L("The notification only appears if Windows notifications are turned on."))
            .Build();

        yield return Tweak.Toggle("startup.restartapps", L("Restart my apps when I sign back in"),
                L("When you sign out or restart, Windows remembers the open apps that support it and restarts them at the next sign-in. Handy for picking up where you left off, but it makes sign-in heavier. Matches “Automatically save my restartable apps and restart them when I sign back in” in Settings › Accounts › Sign-in options."))
            .In(StartupModule.Category, Group)
            .Keywords(L("restart apps, reopen apps, resume apps, restore session, restartable apps"))
            .Tags("lowend", "office")
            .WhenOn(Reg.CuDword(Winlogon, "RestartApps", 1))
            .WhenOff(Reg.CuDword(Winlogon, "RestartApps", 0))
            .Detect(() => RegistryAccess.ReadDword(RegHive.CurrentUser, Winlogon, "RestartApps") == 1 ? TweakDefinition.On : TweakDefinition.Off)
            .WindowsDefault(TweakDefinition.Off)
            .RecommendWhen(p => p.Tier == PerformanceTier.Low || p.RamGb is > 0 and < 6 ? TweakDefinition.Off : null)
            .Build();
    }
}
