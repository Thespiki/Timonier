using Timonier.Core.Catalog;
using Timonier.Core.Platform;
using Timonier.Core.Search;

namespace Timonier.Modules.WindowsTools;

/// <summary>
/// Outils Windows : consoles d'administration, panneaux classiques, dossiers spéciaux et liens directs vers
/// les pages de l'application Paramètres. Aucun réglage, aucune action admin : ce module ne fait qu'ouvrir.
/// </summary>
public sealed class WindowsToolsModule : IModule
{
    public const string PageId = "wintools";
    internal const string Glyph = "";

    public void Register(ModuleRegistry r)
    {
        r.AddPage(new PageInfo(PageId, L("Windows Tools"), Glyph, NavSection.Tools, 40, () => new WindowsToolsPage())
        {
            Description = L("Admin consoles, Control Panel, special folders and shortcuts to every Windows Settings page."),
            Keywords = [L("windows tools, administrative tools, shortcuts, mmc, control panel, windows settings, ms-settings, msc")],
        });

        // Outils (catalogue filtré : fichiers présents et édition compatible ; simples tests d'existence de fichiers).
        foreach (var t in ToolsCatalog.Available)
        {
            if (!t.InSearch) continue;
            r.AddSearchEntry(new SearchEntry
            {
                Id = t.Id,
                Title = t.Title,
                Subtitle = ToolsCatalog.GroupTitle(t.Group) + " · " + t.Command,
                Glyph = t.Glyph,
                Keywords = [.. t.Keywords, t.Command],
                Kind = SearchEntryKind.Tool,
                Execute = t.Launch,
            });
        }

        // Pages des Paramètres Windows (celles propres à Windows 11 sont ignorées sous Windows 10).
        foreach (var link in SettingsCatalog.Links)
        {
            if (!link.InSearch) continue;
            var uri = link.Uri;
            r.AddSearchEntry(new SearchEntry
            {
                Id = link.Id,
                Title = link.Title,
                Subtitle = link.Path,
                Glyph = link.Glyph,
                Keywords = [.. link.Keywords, uri],
                Kind = SearchEntryKind.WindowsSetting,
                Execute = () => ProcessRunner.OpenSettingsUri(uri),
            });
        }

        AddQuickAction(r, "devmgmt", "wintools.quick.devmgmt", L("Drivers, hardware with errors or disabled"), 160);
        AddQuickAction(r, "folder.godmode", "wintools.quick.godmode", L("All Control Panel tasks"), 170);

        // Vocabulaire Windows absent du dictionnaire commun.
        Synonyms.AddGroup("mmc", "console mmc", "composant logiciel enfichable", "snap in", "outils d administration", "administrative tools", "outils windows");
        Synonyms.AddGroup("strategie de groupe", "gpedit", "group policy", "gpo", "strategie locale", "strategies locales");
        Synonyms.AddGroup("mode dieu", "god mode", "godmode", "toutes les taches", "tous les parametres");
        Synonyms.AddGroup("variables d environnement", "environment variables", "variable path", "variables systeme");
        Synonyms.AddGroup("memoire virtuelle", "fichier d echange", "pagefile", "virtual memory", "swap");
        Synonyms.AddGroup("informations systeme", "msinfo32", "system information", "caracteristiques du pc", "specifications", "configuration du pc");
        Synonyms.AddGroup("moniteur de fiabilite", "reliability monitor", "historique de fiabilite");
        Synonyms.AddGroup("moniteur de ressources", "resource monitor", "resmon");
        Synonyms.AddGroup("certificat", "certificate", "certmgr", "autorite de certification");
        Synonyms.AddGroup("gestion des disques", "disk management", "diskmgmt", "partitionner", "lettre de lecteur");
        Synonyms.AddGroup("nom du pc", "nom de l ordinateur", "renommer le pc", "computer name", "hostname");
        Synonyms.AddGroup("defragmenter", "defragmentation", "defrag", "optimiser les lecteurs", "trim");
        Synonyms.AddGroup("fonctionnalites windows", "windows features", "fonctionnalites facultatives", "optional features");
        Synonyms.AddGroup("msconfig", "configuration du systeme", "mode sans echec", "safe mode", "demarrage selectif");
        Synonyms.AddGroup("ne pas deranger", "do not disturb", "concentration", "focus assist", "assistant de concentration");
        Synonyms.AddGroup("projection", "miracast", "projeter sur ce pc", "ecran sans fil", "cast");
        Synonyms.AddGroup("clavier tactile", "touch keyboard", "clavier virtuel", "clavier visuel", "on screen keyboard");
        Synonyms.AddGroup("filtres de couleur", "daltonien", "daltonisme", "color filter", "niveaux de gris");
        Synonyms.AddGroup("narrateur", "lecteur d ecran", "screen reader", "narrator");
        Synonyms.AddGroup("sous titres", "closed captions", "captions", "live captions", "sous titres en direct");
        Synonyms.AddGroup("applications par defaut", "default apps", "navigateur par defaut", "ouvrir avec", "associations de fichiers");
        Synonyms.AddGroup("execution automatique", "autoplay", "lecture automatique");
        Synonyms.AddGroup("activation windows", "cle de produit", "product key", "licence windows", "activer windows");
        Synonyms.AddGroup("localiser mon appareil", "find my device", "pc perdu", "pc vole");
        Synonyms.AddGroup("windows insider", "insider", "preversion", "canal beta", "canal dev");
        Synonyms.AddGroup("gestionnaire d identification", "credential manager", "mots de passe enregistres", "identifiants windows");
        Synonyms.AddGroup("melangeur de volume", "volume mixer", "volume par application", "sndvol");
        Synonyms.AddGroup("eclairage dynamique", "dynamic lighting", "rgb", "led");
        Synonyms.AddGroup("cle d acces", "passkey", "passkeys", "connexion sans mot de passe");
        Synonyms.AddGroup("dossier demarrage", "startup folder", "shell startup");
        Synonyms.AddGroup("envoyer vers", "send to", "sendto");
    }

    private static void AddQuickAction(ModuleRegistry r, string toolKey, string id, string description, int order)
    {
        var tool = ToolsCatalog.Available.FirstOrDefault(t => t.Key == toolKey);
        if (tool is null) return;
        r.AddQuickAction(new QuickAction(id, tool.Title, tool.Glyph, description, () =>
        {
            WindowsToolsUi.Open(tool);
            return Task.CompletedTask;
        })
        {
            Keywords = tool.Keywords,
            Order = order,
        });
    }
}
