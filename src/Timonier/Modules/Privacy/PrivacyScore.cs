using Timonier.Core.Catalog;
using Timonier.Core.Engine;
using Timonier.Core.Model;
using Timonier.Core.Platform;

namespace Timonier.Modules.Privacy;

/// <summary>Un réglage pris en compte dans le score (disponible sur ce PC et doté d'une recommandation).</summary>
public sealed record PrivacyScoreItem(TweakDefinition Tweak, string SectionKey, string Recommended, TweakState State, bool Compliant)
{
    public string RecommendedLabel => Tweak.GetOption(Recommended)?.Label ?? Recommended;
}

/// <summary>Résultat d'une évaluation : réglages notés, états détectés (tous réglages) et réglages non évaluables.</summary>
public sealed class PrivacyScoreResult(IReadOnlyList<PrivacyScoreItem> items, IReadOnlyDictionary<string, TweakState> states, int unavailable)
{
    public IReadOnlyList<PrivacyScoreItem> Items { get; } = items;
    /// <summary>État détecté de chaque réglage de la catégorie (y compris sans recommandation).</summary>
    public IReadOnlyDictionary<string, TweakState> States { get; } = states;
    /// <summary>Réglages inapplicables sur cette édition, version ou configuration matérielle (non comptés).</summary>
    public int Unavailable { get; } = unavailable;

    public int Total => Items.Count;
    public int Compliant => Items.Count(i => i.Compliant);
    public IReadOnlyList<PrivacyScoreItem> Pending => [.. Items.Where(i => !i.Compliant)];
    public double Ratio => Total == 0 ? 1 : (double)Compliant / Total;

    /// <summary>Pourcentage affiché : 100 uniquement si tout est conforme (pas d'arrondi flatteur).</summary>
    public int Percent => Compliant == Total ? 100 : Math.Min(99, (int)Math.Round(Ratio * 100));

    public ScoreLevel Level => Ratio >= 0.8 ? ScoreLevel.Good : Ratio >= 0.5 ? ScoreLevel.Medium : ScoreLevel.Low;

    public (int Compliant, int Total) CountFor(string sectionKey)
    {
        var inSection = Items.Where(i => i.SectionKey == sectionKey).ToList();
        return (inSection.Count(i => i.Compliant), inSection.Count);
    }
}

public enum ScoreLevel { Good, Medium, Low }

/// <summary>
/// Calcul du score de confidentialité : part des réglages recommandés déjà conformes à la recommandation de Timonier
/// pour ce PC. Lecture seule (registre, services, tâches), à exécuter hors du thread UI.
/// </summary>
public static class PrivacyScore
{
    public static PrivacyScoreResult Compute(IEnumerable<TweakDefinition> tweaks, SystemProfile profile, CancellationToken ct = default)
    {
        var items = new List<PrivacyScoreItem>();
        var states = new Dictionary<string, TweakState>(StringComparer.Ordinal);
        var unavailable = 0;
        foreach (var tweak in tweaks)
        {
            ct.ThrowIfCancellationRequested();
            if (tweak.Kind == TweakKind.Action) continue;

            var state = StateDetector.Detect(tweak);
            states[tweak.Id] = state;

            // Réglage inapplicable ici (édition, version, matériel) : jamais compté, qu'il ait ou non une recommandation.
            if (tweak.Requirement.Check(profile) is not null)
            {
                unavailable++;
                continue;
            }
            var recommended = tweak.RecommendationFor(profile);
            if (recommended is null || tweak.GetOption(recommended) is null) continue;
            var compliant = !state.Unknown && !state.Partial && state.OptionKey == recommended;
            var section = PrivacyGroups.ByGroup(tweak.Group)?.Key ?? "";
            items.Add(new PrivacyScoreItem(tweak, section, recommended, state, compliant));
        }
        return new PrivacyScoreResult(items, states, unavailable);
    }

    /// <summary>Traduction du score en contrôle de santé (tableau de bord).</summary>
    public static HealthResult ToHealth(PrivacyScoreResult result)
    {
        if (result.Total == 0)
            return new HealthResult(HealthStatus.Unknown, L("Aucun réglage de confidentialité évaluable sur ce PC."));

        var status = result.Level switch
        {
            ScoreLevel.Good => HealthStatus.Good,
            ScoreLevel.Medium => HealthStatus.Info,
            _ => HealthStatus.Warning,
        };
        var summary = L("{0} % des réglages au niveau recommandé ({1} sur {2})", result.Percent, result.Compliant, result.Total);
        var pending = result.Pending;
        string detail;
        if (pending.Count == 0)
        {
            detail = L("Tous les réglages évalués suivent la recommandation de Timonier pour ce PC.");
        }
        else
        {
            var names = string.Join(", ", pending.Take(4).Select(p => p.Tweak.Title));
            detail = pending.Count > 4
                ? LP(pending.Count - 4, "À revoir : {1} et {0} autre.", "À revoir : {1} et {0} autres.", names)
                : L("À revoir : {0}.", names);
        }
        return new HealthResult(status, summary, detail);
    }
}
