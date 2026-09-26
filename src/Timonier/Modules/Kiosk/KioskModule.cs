using Timonier.Core.Catalog;
using Timonier.Core.Platform;
using Timonier.Core.Search;

namespace Timonier.Modules.Kiosk;

/// <summary>
/// Mode kiosque : assistant pas à pas pour transformer le PC en borne (accès attribué pour une application du Store,
/// Edge ou programme classique comme interface d'un compte standard), restrictions, ouverture automatique, état et retrait.
/// Déclarations uniquement : appelé aussi dans le broker.
/// </summary>
public sealed class KioskModule : IModule
{
    public const string PageId = "kiosk";
    internal const string Glyph = "";

    public void Register(ModuleRegistry r)
    {
        r.AddAction(new CreateKioskAccountAction());
        r.AddAction(new ApplyKioskAction());
        r.AddAction(new RemoveKioskAction());
        r.AddAction(new KioskStatusAction());
        r.AddAction(new SetAutologonAction());
        r.AddAction(new ClearAutologonAction());

        r.AddPage(new PageInfo(PageId, L("Kiosk mode"),Glyph, NavSection.Control, 40, () => new KioskPage())
        {
            Description = L("Turn this PC into a kiosk dedicated to a single app, then undo it in one click."),
            Keywords = [L("kiosk, kiosk mode, assigned access, single app, public display, interactive kiosk, shell, automatic sign-in, autologon")],
        });

        r.AddQuickAction(new QuickAction("kiosk.open", L("Turn this PC into a kiosk"), Glyph,
            L("Step-by-step wizard: dedicated account, single app, restrictions and automatic sign-in."),
            () => { AppHost.Navigator.Navigate(PageId); return Task.CompletedTask; })
        {
            Keywords = [L("kiosk, kiosk mode, single app, public display, kiosk terminal")],
            Order = 80,
        });

        r.AddSearchEntry(new SearchEntry
        {
            Id = "kiosk.wizard", Title = L("Kiosk mode wizard"),Subtitle = L("Dedicated account, single app, restrictions"),
            Glyph = Glyph, PageId = PageId, PageParameter = "section:wizard", Boost = 0.1,
            Keywords = [L("kiosk, kiosk mode, assigned access, full-screen app, single app")],
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "kiosk.edge", Title = L("Microsoft Edge in kiosk mode"), Subtitle = L("Public display or public browsing on a web address"),
            Glyph = "", PageId = PageId, PageParameter = "mode:edge",
            Keywords = [L("edge kiosk, browser kiosk, digital signage, public browsing, full-screen website")],
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "kiosk.shell", Title = L("Replace the desktop with an app"), Subtitle = L("Custom interface for an account (alternative to Shell Launcher)"),
            Glyph = "", PageId = PageId, PageParameter = "mode:win32",
            Keywords = [L("custom shell, shell launcher, replace explorer, custom user interface")],
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "kiosk.remove", Title = L("Turn off kiosk mode"), Subtitle = L("Give the desktop back to the kiosk account and remove restrictions"),
            Glyph = "", PageId = PageId, PageParameter = "section:status",
            Keywords = [L("exit kiosk, disable kiosk, leave kiosk mode, clear assigned access, remove kiosk")],
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "kiosk.autologon", Title = L("Automatic sign-in"), Subtitle = L("Sign in to the kiosk account at startup (password protected by LSA)"),
            Glyph = "", PageId = PageId, PageParameter = "section:status",
            Keywords = [L("autologon, automatic sign-in, auto login, automatic logon, auto sign in, no password at startup")],
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "kiosk.win.assignedaccess", Title = L("Kiosk (Windows Settings)"), Subtitle = L("Opens Settings › Accounts › Other users › Kiosk"),
            Glyph = Glyph, Kind = SearchEntryKind.WindowsSetting,
            Keywords = [L("assigned access, set up a kiosk, kiosk settings")],
            Execute = () => ProcessRunner.OpenSettingsUri("ms-settings:assignedaccess"),
        });

        Synonyms.AddGroup("kiosque", "kiosk", "borne", "borne interactive", "mode kiosque", "assigned access", "acces attribue");
        Synonyms.AddGroup("ouverture de session automatique", "autologon", "connexion automatique", "auto login", "autoadminlogon");
    }
}
