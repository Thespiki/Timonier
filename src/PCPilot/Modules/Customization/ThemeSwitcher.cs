using PcPilot.Core.Catalog;
using PcPilot.Core.Model;
using PcPilot.Core.Platform;
using PcPilot.UI.Services;

namespace PcPilot.Modules.Customization;

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

    /// <summary>Applique un couple (applications, Windows). <paramref name="label"/> : « clair », « sombre », « mixte ».</summary>
    public static async Task<bool> ApplyAsync(string apps, string system, string label)
    {
        var items = new List<(TweakDefinition Tweak, string Option)>();
        if (AppHost.Registry.GetTweak(SystemId) is { } s && AppHost.Engine.Detect(s).OptionKey != system) items.Add((s, system));
        if (AppHost.Registry.GetTweak(AppsId) is { } a && AppHost.Engine.Detect(a).OptionKey != apps) items.Add((a, apps));
        if (items.Count == 0)
        {
            AppHost.Toasts.Show($"Le mode {label} est déjà actif.", ToastKind.Info);
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

        if (ids.Count > 0)
            AppHost.Toasts.Show($"Mode {label} appliqué.", ToastKind.Success, "Annuler", () => _ = UndoAsync(ids, silent: false));
        else
            AppHost.Toasts.Show($"Mode {label} appliqué.", ToastKind.Success);
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
        if (ok && !silent) AppHost.Toasts.Show("Mode précédent rétabli.", ToastKind.Success);
    }

    /// <summary>Action rapide du tableau de bord : sombre si l'on est en clair, clair sinon.</summary>
    public static QuickAction CreateQuickAction() =>
        new("custom.toggle-theme", "Basculer clair / sombre", "",
            "Passe Windows et les applications en mode sombre, ou revient au mode clair.",
            async () =>
            {
                if (AppsLight()) await ApplyAsync("dark", "dark", "sombre");
                else await ApplyAsync("light", "light", "clair");
            })
        {
            Keywords = ["mode sombre", "mode clair", "dark mode", "light mode", "theme", "basculer", "nuit"],
            Order = 40,
        };
}
