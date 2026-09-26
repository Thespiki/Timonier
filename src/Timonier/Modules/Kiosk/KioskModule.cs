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

        r.AddPage(new PageInfo(PageId, L("Mode kiosque"),Glyph, NavSection.Control, 40, () => new KioskPage())
        {
            Description = L("Transformer ce PC en borne dédiée à une seule application, puis revenir en arrière en un clic."),
            Keywords = [L("kiosque, borne, kiosk, accès attribué, assigned access, application unique, affichage public, borne interactive, mode borne, shell, ouverture de session automatique")],
        });

        r.AddQuickAction(new QuickAction("kiosk.open", L("Transformer ce PC en borne"), Glyph,
            L("Assistant pas à pas : compte dédié, application unique, restrictions et ouverture automatique."),
            () => { AppHost.Navigator.Navigate(PageId); return Task.CompletedTask; })
        {
            Keywords = [L("kiosque, borne, kiosk, mode kiosque, application unique, affichage public")],
            Order = 80,
        });

        r.AddSearchEntry(new SearchEntry
        {
            Id = "kiosk.wizard", Title = L("Assistant mode kiosque"),Subtitle = L("Compte dédié, application unique, restrictions"),
            Glyph = Glyph, PageId = PageId, PageParameter = "section:wizard", Boost = 0.1,
            Keywords = [L("kiosque, borne, kiosk mode, assigned access, acces attribue, application plein ecran")],
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "kiosk.edge", Title = L("Microsoft Edge en mode kiosque"), Subtitle = L("Affichage public ou navigation publique sur une adresse web"),
            Glyph = "", PageId = PageId, PageParameter = "mode:edge",
            Keywords = [L("edge kiosque, navigateur kiosque, affichage dynamique, digital signage, navigation publique, site web plein ecran")],
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "kiosk.shell", Title = L("Remplacer le Bureau par une application"), Subtitle = L("Interface personnalisée pour un compte (alternative à Shell Launcher)"),
            Glyph = "", PageId = PageId, PageParameter = "mode:win32",
            Keywords = [L("shell personnalise, custom shell, shell launcher, remplacer explorer, interface utilisateur personnalisee")],
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "kiosk.remove", Title = L("Désactiver le mode kiosque"), Subtitle = L("Rendre le Bureau au compte kiosque et retirer les restrictions"),
            Glyph = "", PageId = PageId, PageParameter = "section:status",
            Keywords = [L("quitter kiosque, desactiver borne, sortir du mode kiosque, clear assigned access, retirer kiosque")],
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "kiosk.autologon", Title = L("Ouverture de session automatique"), Subtitle = L("Ouvrir la session du compte kiosque au démarrage (mot de passe protégé par LSA)"),
            Glyph = "", PageId = PageId, PageParameter = "section:status",
            Keywords = [L("autologon, connexion automatique, ouverture automatique, auto login, session automatique, sans mot de passe au demarrage")],
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "kiosk.win.assignedaccess", Title = L("Kiosque (Paramètres Windows)"), Subtitle = L("Ouvre Paramètres › Comptes › Autres utilisateurs › Kiosque"),
            Glyph = Glyph, Kind = SearchEntryKind.WindowsSetting,
            Keywords = [L("assigned access, acces attribue, configurer un kiosque, parametres kiosque")],
            Execute = () => ProcessRunner.OpenSettingsUri("ms-settings:assignedaccess"),
        });

        Synonyms.AddGroup("kiosque", "kiosk", "borne", "borne interactive", "mode kiosque", "assigned access", "acces attribue");
        Synonyms.AddGroup("ouverture de session automatique", "autologon", "connexion automatique", "auto login", "autoadminlogon");
    }
}
