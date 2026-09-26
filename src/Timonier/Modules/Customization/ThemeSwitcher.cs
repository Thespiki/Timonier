using Timonier.Core.Catalog;
using Timonier.Core.Model;
using Timonier.Core.Platform;
using Timonier.UI.Services;

namespace Timonier.Modules.Customization;

/// <summary>
/// Bascule du mode clair/sombre (thème de Windows + thème des applications) via les réglages déclaratifs
/// <c>custom.theme.system</c> et <c>custom.theme.apps</c> : chaque changement est journalisé et annulable.
/// Interface uniquement (utilise AppHost).
/// </summary>
internal static class ThemeSwitcher
{
    public const string AppsId = "custom.theme.apps";
    public const string SystemId = "custom.theme.system";

    /// <summary>Mode des applications : clair si la valeur est absente (valeur par défaut de Windows).</summary>
    public static bool AppsLight() =>
        RegistryAccess.ReadDword(RegHive.CurrentUser, CustomizationTweaks.Personalize, "AppsUseLightTheme") is not 0;

    /// <summary>Mode de Windows : absent = clair sous Windows 11, sombre sous Windows 10.</summary>
    public static bool SystemLight() =>
        RegistryAccess.ReadDword(RegHive.CurrentUser, CustomizationTweaks.Personalize, "SystemUsesLightTheme") is { } v ? v != 0 : OsInfo.Build >= 22000;

    /// <summary>Applique un couple (applications, Windows), "light" ou "dark" pour chacun (mode affiché, voir <see cref="ModeName"/> :« clair », « sombre », « mixte »).</summary>
    public static async Task<bool> ApplyAsync(string apps, string system)
    {
        var mode = ModeKey(apps, system);
        var items = new List<(TweakDefinition Tweak, string Option)>();
        if (AppHost.Registry.GetTweak(SystemId) is { } s && AppHost.Engine.Detect(s).OptionKey != system) items.Add((s, system));
        if (AppHost.Registry.GetTweak(AppsId) is { } a && AppHost.Engine.Detect(a).OptionKey != apps) items.Add((a, apps));
        if (items.Count == 0)
        {
            AppHost.Toasts.Show(mode switch
            {
                "light" => L("Le mode clair est déjà actif."),
                "dark" => L("Le mode sombre est déjà actif."),
                _ => L("Le mode mixte est déjà actif."),
            }, ToastKind.Info);
            return true;
        }

        var results = await AppHost.Engine.ApplyManyAsync(items);
        var failed = results.FirstOrDefault(r => !r.Outcome.Success);
        var ids = results.Where(r => r.Outcome.Success).Select(r => r.Outcome.JournalId).OfType<Guid>().ToList();
        if (failed.Tweak is not null)
        {
            if (ids.Count > 0) await UndoAsync(ids, silent: true); // pas de thème à moitié appliqué
            AppHost.Toasts.ShowOutcome(failed.Outcome);
            return false;
        }

        var applied = mode switch
        {
            "light" => L("Mode clair appliqué."),
            "dark" => L("Mode sombre appliqué."),
            _ => L("Mode mixte appliqué."),
        };
        if (ids.Count > 0)
            AppHost.Toasts.Show(applied, ToastKind.Success, L("Annuler"), () => _ = UndoAsync(ids, silent: false));
        else
            AppHost.Toasts.Show(applied, ToastKind.Success);
        return true;
    }

    private static async Task UndoAsync(List<Guid> ids, bool silent)
    {
        var journal = AppHost.Engine.JournalAll();
        var ok = true;
        foreach (var id in Enumerable.Reverse(ids))
        {
            if (journal.FirstOrDefault(e => e.Id == id) is not { } entry || !entry.CanUndo) continue;
            var outcome = await AppHost.Engine.UndoAsync(entry);
            if (!outcome.Success)
            {
                ok = false;
                AppHost.Toasts.ShowOutcome(outcome);
            }
        }
        if (ok && !silent) AppHost.Toasts.Show(L("Mode précédent rétabli."), ToastKind.Success);
    }

    /// <summary>Mode résultant d'un couple (applications, Windows) : "light", "dark" ou "mixed" (identifiant stable).</summary>
    private static string ModeKey(string apps, string system) => apps == system ? apps : "mixed";

    /// <summary>Nom affiché du mode (Mode clair, Mode sombre, Mode mixte).</summary>
    public static string ModeName(string apps, string system) => ModeKey(apps, system) switch
    {
        "light" => L("Mode clair"),
        "dark" => L("Mode sombre"),
        _ => L("Mode mixte"),
    };

    /// <summary>Action rapide du tableau de bord : sombre si l'on est en clair, clair sinon.</summary>
    public static QuickAction CreateQuickAction() =>
        new("custom.toggle-theme", L("Basculer clair / sombre"), "",
            L("Passe Windows et les applications en mode sombre, ou revient au mode clair."),
            async () =>
            {
                if (AppsLight()) await ApplyAsync("dark", "dark");
                else await ApplyAsync("light", "light");
            })
        {
            Keywords = [L("mode sombre, mode clair, dark mode, light mode, theme, basculer, nuit")],
            Order = 40,
        };
}
