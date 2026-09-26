using Timonier.Core.Catalog;
using Timonier.Core.Model;
using Timonier.UI.Services;

namespace Timonier.Modules.Performance;

/// <summary>Contrôle de santé « Alimentation » (lecture seule, hors thread UI).</summary>
public static class PowerHealth
{
    public static HealthResult Check()
    {
        try
        {
            var schemes = PowerApi.EnumerateSchemes();
            var active = schemes.FirstOrDefault(s => s.IsActive);
            var source = PowerApi.GetSource();
            var mode = PowerApi.GetEffectiveMode() is { } g ? PowerApi.ModeFromGuid(g) : null;

            var plan = active is null ? L("Plan inconnu")
                : active.IsBalanced && mode is not null ? L("Plan « {0} », mode {1}", active.Name, mode.Label)
                : L("Plan « {0} »", active.Name);
            var summary = !source.HasBattery ? plan
                : source.OnBattery
                    ? (source.BatteryPercent is { } b ? L("{0} · sur batterie ({1} %)", plan, b) : L("{0} · sur batterie", plan))
                    : (source.BatteryPercent is { } c ? L("{0} · sur secteur ({1} %)", plan, c) : L("{0} · sur secteur", plan));

            var hungry = active?.IsHighPerformance == true
                         || active?.IsBalanced == true && (mode == PowerApi.ModePerformance || mode == PowerApi.ModeBetterPerformance);
            if (source.OnBattery && hungry)
            {
                return new HealthResult(HealthStatus.Warning, L("Sur batterie en mode performances"),
                    L("{0}. L'autonomie est réduite : passez en mode Équilibré ou Économie sur batterie.", summary));
            }
            return new HealthResult(HealthStatus.Info, summary);
        }
        catch (Exception ex)
        {
            return new HealthResult(HealthStatus.Unknown, L("État de l'alimentation indisponible"), ex.Message);
        }
    }
}

/// <summary>Actions rapides du tableau de bord (exécutées sur le thread UI).</summary>
public static class PerfQuickActions
{
    public static async Task GameModeAsync()
    {
        var profile = AppHost.Profile;
        var wanted = new List<(TweakDefinition Tweak, string Option)>();
        void Want(string id, string? option)
        {
            if (option is null || AppHost.Registry.GetTweak(id) is not { } t) return;
            if (AppHost.Engine.Unavailability(t) is not null) return;
            wanted.Add((t, option));
        }
        Want("perf.game.mode", TweakDefinition.On);
        Want("perf.game.bgrecord", TweakDefinition.Off);
        if (AppHost.Registry.GetTweak("perf.game.hags") is { } hags) Want(hags.Id, hags.RecommendationFor(profile));

        // Ne garder que ce qui diffère de l'état actuel.
        var pending = new List<(TweakDefinition Tweak, string Option)>();
        foreach (var w in wanted)
        {
            var state = await AppHost.Engine.DetectAsync(w.Tweak);
            if (state.OptionKey != w.Option) pending.Add(w);
        }

        if (pending.Count == 0)
        {
            AppHost.Toasts.Show(L("Ce PC est déjà configuré pour les jeux : Mode Jeu actif, pas d'enregistrement en arrière-plan."), ToastKind.Success);
            return;
        }

        var lines = string.Join("\n", pending.Select(p => $"• {p.Tweak.Title} → {p.Tweak.GetOption(p.Option)?.Label}"));
        var admin = pending.Any(p => p.Tweak.RequiresAdmin);
        if (!await AppHost.Dialogs.ConfirmAsync(L("Mode jeu"),
                admin
                    ? L("Les réglages suivants vont être appliqués :\n\n{0}\n\nUne autorisation administrateur sera demandée.\nTout reste annulable depuis le Journal.", lines)
                    : L("Les réglages suivants vont être appliqués :\n\n{0}\n\nTout reste annulable depuis le Journal.", lines),
                L("Appliquer")))
            return;

        var results = await AppHost.Engine.ApplyManyAsync(pending);
        var failed = results.Where(r => !r.Outcome.Success).ToList();
        var effects = results.Where(r => r.Outcome.Success).Aggregate(ApplyEffect.None, (acc, r) => acc | r.Tweak.Effect);
        if (failed.Count == 0)
            AppHost.Toasts.ShowOutcome(new Core.Engine.ApplyOutcome(true, LP(results.Count, "Mode jeu : {0} réglage appliqué.", "Mode jeu : {0} réglages appliqués."), effects));
        else
            AppHost.Toasts.Show(LP(failed.Count, "Mode jeu : {0} réglage en échec — {1}", "Mode jeu : {0} réglages en échec — {1}",
                string.Join(" ; ", failed.Select(f => f.Tweak.Title + " (" + f.Outcome.Message + ")"))),
                ToastKind.Warning);
    }

    public static async Task EcoModeAsync()
    {
        var (schemes, source, modeApi) = await Task.Run(() => (PowerApi.EnumerateSchemes(), PowerApi.GetSource(), PowerApi.UserModeApiAvailable));
        var active = schemes.FirstOrDefault(s => s.IsActive);

        // 1. Plan « Utilisation normale » + Windows 11 : mode « Meilleure efficacité énergétique » pour la source actuelle.
        if (active?.IsBalanced == true && modeApi)
        {
            var outcome = await AppHost.Engine.RunActionAsync(SetPowerModeAction.ActionId, new Dictionary<string, string>
            {
                ["source"] = source.OnBattery ? "dc" : "ac",
                ["mode"] = PowerApi.ModeEfficiency.Key,
            });
            AppHost.Toasts.ShowOutcome(outcome);
            return;
        }

        // 2. Sinon, plan Économie d'énergie s'il existe.
        if (schemes.FirstOrDefault(s => s.IsPowerSaver) is { } saver)
        {
            if (saver.IsActive)
            {
                AppHost.Toasts.Show(L("Le plan « {0} » est déjà actif.", saver.Name), ToastKind.Info);
                return;
            }
            AppHost.Toasts.ShowOutcome(await PowerUi.ActivateSchemeAsync(saver.Id));
            return;
        }

        // 3. Aucun moyen direct : Paramètres Windows.
        AppHost.Toasts.Show(L("Aucun plan Économie d'énergie sur ce PC : ouverture des paramètres d'alimentation de Windows."), ToastKind.Info);
        try { Core.Platform.ProcessRunner.OpenSettingsUri("ms-settings:powersleep"); }
        catch (Exception ex) { AppHost.Toasts.Show(ex.Message, ToastKind.Error); }
    }
}

/// <summary>Opérations d'alimentation partagées par la page et les actions rapides (thread UI).</summary>
public static class PowerUi
{
    /// <summary>Active un plan sans élévation ; ne passe par l'administrateur que si Windows refuse l'accès.</summary>
    public static async Task<Core.Engine.ApplyOutcome> ActivateSchemeAsync(Guid scheme)
    {
        var p = new Dictionary<string, string> { ["scheme"] = scheme.ToString() };
        var outcome = await AppHost.Engine.RunActionAsync(ActivateSchemeAction.UserId, p);
        if (!outcome.Success && outcome.Data?.GetValueOrDefault("code") == PowerApi.ErrorAccessDenied.ToString())
            outcome = await AppHost.Engine.RunActionAsync(ActivateSchemeAction.AdminId, p);
        return outcome;
    }
}
