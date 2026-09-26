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

            var plan = active is null ? L("Unknown plan")
                : active.IsBalanced && mode is not null ? L("“{0}” plan, {1} mode", active.Name, mode.Label)
                : L("“{0}” plan", active.Name);
            var summary = !source.HasBattery ? plan
                : source.OnBattery
                    ? (source.BatteryPercent is { } b ? L("{0} · on battery ({1}%)", plan, b) : L("{0} · on battery", plan))
                    : (source.BatteryPercent is { } c ? L("{0} · plugged in ({1}%)", plan, c) : L("{0} · plugged in", plan));

            var hungry = active?.IsHighPerformance == true
                         || active?.IsBalanced == true && (mode == PowerApi.ModePerformance || mode == PowerApi.ModeBetterPerformance);
            if (source.OnBattery && hungry)
            {
                return new HealthResult(HealthStatus.Warning, L("On battery in performance mode"),
                    L("{0}. Battery life is reduced: switch to Balanced or power saving mode on battery.", summary));
            }
            return new HealthResult(HealthStatus.Info, summary);
        }
        catch (Exception ex)
        {
            return new HealthResult(HealthStatus.Unknown, L("Power status unavailable"), ex.Message);
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
            AppHost.Toasts.Show(L("This PC is already set up for gaming: Game Mode on, no background recording."), ToastKind.Success);
            return;
        }

        var lines = string.Join("\n", pending.Select(p => $"• {p.Tweak.Title} → {p.Tweak.GetOption(p.Option)?.Label}"));
        var admin = pending.Any(p => p.Tweak.RequiresAdmin);
        if (!await AppHost.Dialogs.ConfirmAsync(L("Gaming mode"),
                admin
                    ? L("The following settings will be applied:\n\n{0}\n\nAdministrator permission will be requested.\nEverything can still be undone from History.", lines)
                    : L("The following settings will be applied:\n\n{0}\n\nEverything can still be undone from History.", lines),
                L("Apply")))
            return;

        var results = await AppHost.Engine.ApplyManyAsync(pending);
        var failed = results.Where(r => !r.Outcome.Success).ToList();
        var effects = results.Where(r => r.Outcome.Success).Aggregate(ApplyEffect.None, (acc, r) => acc | r.Tweak.Effect);
        if (failed.Count == 0)
            AppHost.Toasts.ShowOutcome(new Core.Engine.ApplyOutcome(true, LP(results.Count, "Gaming mode: {0} setting applied.", "Gaming mode: {0} settings applied."), effects));
        else
            AppHost.Toasts.Show(LP(failed.Count, "Gaming mode: {0} setting failed — {1}", "Gaming mode: {0} settings failed — {1}",
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
                AppHost.Toasts.Show(L("The “{0}” plan is already active.", saver.Name), ToastKind.Info);
                return;
            }
            AppHost.Toasts.ShowOutcome(await PowerUi.ActivateSchemeAsync(saver.Id));
            return;
        }

        // 3. Aucun moyen direct : Paramètres Windows.
        AppHost.Toasts.Show(L("No Power saver plan on this PC: opening Windows power settings."), ToastKind.Info);
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
