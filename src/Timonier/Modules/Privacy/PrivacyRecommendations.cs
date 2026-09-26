using System.Windows;
using System.Windows.Controls;
using Timonier.Core.Engine;
using Timonier.Core.Model;
using Timonier.Core.Platform;
using Timonier.UI.Services;

namespace Timonier.Modules.Privacy;

/// <summary>
/// « Appliquer le niveau recommandé » : analyse, confirmation détaillée (liste des changements), application groupée
/// (une seule invite UAC) et compte rendu. Interface uniquement (thread UI) : utilisé par la page et l'action rapide.
/// </summary>
public static class PrivacyRecommendations
{
    private static bool _running;

    /// <summary>
    /// Lance le flux complet. <paramref name="onApplying"/> reçoit la liste des réglages au moment où l'application commence
    /// (après confirmation) ; la valeur renvoyée est la liste des réglages effectivement tentés (vide si rien n'a été fait).
    /// </summary>
    public static async Task<IReadOnlyList<TweakDefinition>> RunAsync(IProgress<string>? progress = null,
        Action<IReadOnlyList<TweakDefinition>>? onApplying = null)
    {
        if (_running)
        {
            AppHost.Toasts.Show(L("The recommended level is already being applied."), ToastKind.Info);
            return [];
        }
        _running = true;
        try
        {
            var tweaks = AppHost.Registry.TweaksIn(PrivacyModule.Category).ToList();
            var profile = AppHost.Profile;
            PrivacyScoreResult result;
            try
            {
                result = await Task.Run(() => PrivacyScore.Compute(tweaks, profile));
            }
            catch (Exception ex)
            {
                Log.Error("Privacy", "analyse avant application", ex);
                AppHost.Toasts.Show(L("Couldn't scan the privacy settings: {0}", ex.Message), ToastKind.Error);
                return [];
            }

            var pending = result.Pending;
            if (pending.Count == 0)
            {
                AppHost.Toasts.Show(L("All privacy settings already follow the recommendation for this PC."), ToastKind.Success);
                return [];
            }

            var confirmed = await AppHost.Dialogs.ShowAsync(L("Apply the recommended level"), BuildConfirmation(pending),
                L("Apply ({0})", pending.Count), L("Undo"));
            if (!confirmed) return [];

            var targets = pending.Select(p => p.Tweak).ToList();
            onApplying?.Invoke(targets);
            var results = await AppHost.Engine.ApplyManyAsync(pending.Select(p => (p.Tweak, p.Recommended)), progress);
            Report(results);
            return targets;
        }
        finally
        {
            _running = false;
        }
    }

    private static void Report(List<(TweakDefinition Tweak, ApplyOutcome Outcome)> results)
    {
        var ok = results.Count(r => r.Outcome.Success);
        var cancelled = results.FirstOrDefault(r => r.Outcome.Cancelled);
        var failed = results.Where(r => !r.Outcome.Success && !r.Outcome.Cancelled).ToList();

        if (cancelled.Tweak is not null)
        {
            AppHost.Toasts.Show(ok == 0
                    ? L("Apply canceled: no settings were changed.")
                    : LP(ok, "Apply interrupted: {0} setting changed before the interruption.",
                        "Apply interrupted: {0} settings changed before the interruption."),
                ToastKind.Info);
        }
        else if (failed.Count == 0)
        {
            AppHost.Toasts.Show(LP(ok, "{0} privacy setting applied. Every change can be undone from History.",
                    "{0} privacy settings applied. Every change can be undone from History."),
                ToastKind.Success);
        }
        else
        {
            var details = string.Join(" ; ", failed.Take(3).Select(f => $"{f.Tweak.Title} ({f.Outcome.Message})"));
            if (failed.Count > 3) details = LP(failed.Count - 3, "{1}; and {0} more", "{1}; and {0} more", details);
            AppHost.Toasts.Show(LP(ok, "{0} setting applied, {1} failed: {2}", "{0} settings applied, {1} failed: {2}",
                failed.Count, details), ToastKind.Warning,
                duration: TimeSpan.FromSeconds(12));
        }

        var effects = results.Where(r => r.Outcome.Success).Aggregate(ApplyEffect.None, (acc, r) => acc | r.Tweak.Effect);
        if (effects != ApplyEffect.None) AppHost.Toasts.ShowOutcome(new ApplyOutcome(true, "", effects));
    }

    /// <summary>Contenu de la boîte de confirmation : changements groupés par section, avertissements et effets.</summary>
    private static FrameworkElement BuildConfirmation(IReadOnlyList<PrivacyScoreItem> pending)
    {
        var panel = new StackPanel();
        panel.Children.Add(Text(LP(pending.Count,
            "{0} setting will be changed to follow Timonier's recommendation for this PC:",
            "{0} settings will be changed to follow Timonier's recommendation for this PC:"), "Pp.Body"));

        foreach (var section in PrivacyGroups.All)
        {
            var items = pending.Where(p => p.SectionKey == section.Key).ToList();
            if (items.Count == 0) continue;

            var header = Text(section.Title, "Pp.Body");
            header.FontWeight = FontWeights.SemiBold;
            header.Margin = new Thickness(0, 12, 0, 4);
            panel.Children.Add(header);

            foreach (var item in items)
            {
                panel.Children.Add(Bullet($"{item.Tweak.Title} → {item.RecommendedLabel}"));
                if (item.Tweak.Warning is { } warning)
                {
                    var w = Text(warning, "Pp.Caption");
                    w.SetResourceReference(TextBlock.ForegroundProperty, "Pp.Warning");
                    w.Margin = new Thickness(16, 0, 0, 4);
                    panel.Children.Add(w);
                }
            }
        }

        var notes = new List<string>();
        if (pending.Any(p => p.Tweak.RequiresAdmin))
            notes.Add(L("Some settings protect the whole PC: Windows will ask for administrator permission (UAC) only once."));
        var effects = pending.Aggregate(ApplyEffect.None, (acc, p) => acc | p.Tweak.Effect);
        if (effects.HasFlag(ApplyEffect.Reboot)) notes.Add(L("Some changes will only take effect after the PC restarts."));
        else if (effects.HasFlag(ApplyEffect.SignOut)) notes.Add(L("Some changes will only take effect the next time you sign in."));
        else if (effects.HasFlag(ApplyEffect.RestartExplorer)) notes.Add(L("Windows Explorer will need to be restarted for some changes (offered afterward)."));
        notes.Add(L("Every change can be undone from Timonier's History."));

        var footer = Text(string.Join("\n", notes), "Pp.Caption");
        footer.Margin = new Thickness(0, 14, 0, 0);
        panel.Children.Add(footer);
        return panel;
    }

    private static TextBlock Text(string text, string style)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(FrameworkElement.StyleProperty, style);
        return t;
    }

    private static FrameworkElement Bullet(string text)
    {
        var grid = new Grid { Margin = new Thickness(4, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var dot = Text("•", "Pp.Body");
        dot.FontSize = 13;
        var body = Text(text, "Pp.Body");
        body.FontSize = 13;
        Grid.SetColumn(body, 1);
        grid.Children.Add(dot);
        grid.Children.Add(body);
        return grid;
    }
}
