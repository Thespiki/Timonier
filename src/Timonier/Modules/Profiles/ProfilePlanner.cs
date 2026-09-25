using Timonier.Core.Catalog;
using Timonier.Core.Engine;
using Timonier.Core.Model;
using Timonier.Core.Platform;
using Timonier.Modules.Apps;

namespace Timonier.Modules.Profiles;

/// <summary>Une demande d'option pour un réglage, et qui la formule (profil ou fichier importé).</summary>
internal sealed record TweakWant(string SourceId, string SourceTitle, string Option);

/// <summary>Ligne du plan : un réglage, son état actuel et l'option visée.</summary>
internal sealed class PlanTweak
{
    public required TweakDefinition Tweak { get; init; }
    public required string CategoryTitle { get; init; }
    public int CategoryOrder { get; init; }
    public int Order { get; init; }
    public List<TweakWant> Wants { get; } = [];
    public string Target { get; set; } = "";
    public bool Selected { get; set; }
    /// <summary>Proposé mais laissé décoché par défaut (profil « à cocher en connaissance de cause » ou risque avancé).</summary>
    public bool OptIn { get; set; }
    /// <summary>Réglage de sécurité importé dont l'option s'écarte de la recommandation pour ce PC.</summary>
    public bool LessSafe { get; set; }
    public TweakState? Current { get; set; }
    public string? Unavailable { get; set; }

    public bool HasConflict => Wants.Select(w => w.Option).Distinct(StringComparer.Ordinal).Count() > 1;
    public bool AtTarget => Current is { Unknown: false, Partial: false } c && c.OptionKey == Target;
    public string TargetLabel => Tweak.GetOption(Target)?.Label ?? Target;

    public string CurrentLabel => Current switch
    {
        null => "…",
        { Unknown: true } => Tweak.Kind == TweakKind.Action ? "Action ponctuelle" : "État inconnu",
        { OptionKey: { } k, Partial: true } => (Tweak.GetOption(k)?.Label ?? k) + " (partiel)",
        { OptionKey: { } k } => Tweak.GetOption(k)?.Label ?? k,
        _ => "État inconnu",
    };
}

/// <summary>Application proposée par le plan.</summary>
internal sealed class PlanApp
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public bool InCatalog { get; init; }
    public bool Installed { get; set; }
    public bool Selected { get; set; }
    /// <summary>Cochée par un profil (ou présente dans le fichier importé).</summary>
    public bool Preselected { get; set; }
    public List<string> Sources { get; } = [];
}

/// <summary>Plan importé d'un fichier (données NON fiables : tout repasse par l'étape de vérification).</summary>
internal sealed class ImportedPlan
{
    public required string FileName { get; init; }
    public List<string> Profiles { get; } = [];
    public Dictionary<string, string> Tweaks { get; } = new(StringComparer.Ordinal);
    public List<string> Apps { get; } = [];
    public List<string> Notices { get; } = [];
}

internal sealed class ProfilePlan
{
    public List<PlanTweak> Tweaks { get; } = [];
    public List<PlanTweak> Unavailable { get; } = [];
    public List<PlanApp> Apps { get; } = [];
    public List<string> Notices { get; } = [];
    public int SkippedHeavyApps { get; set; }
    public bool InstalledAppsKnown { get; set; }
    public IReadOnlyList<ProfileDefinition> Profiles { get; init; } = [];
    public ImportedPlan? Import { get; init; }

    public IEnumerable<PlanTweak> Actionable => Tweaks.Where(t => !t.AtTarget);
    public IEnumerable<PlanTweak> SelectedTweaks => Tweaks.Where(t => t.Selected && !t.AtTarget);
    public IEnumerable<PlanApp> SelectedApps => Apps.Where(a => a.Selected && !a.Installed);
}

/// <summary>Calcul du plan (hors du thread d'interface) à partir des profils choisis ou d'un fichier importé.</summary>
internal static class ProfilePlanner
{
    public const string ImportSource = "import";

    /// <summary>Réglages retenus par un profil, avec l'option visée (null = non inclus).</summary>
    public static IEnumerable<(TweakDefinition Tweak, string Option)> Resolve(ProfileDefinition profile, ModuleRegistry registry, SystemProfile pc)
    {
        foreach (var t in registry.Tweaks)
        {
            if (profile.Excluded.Contains(t.Id)) continue;
            if (profile.When.TryGetValue(t.Id, out var condition) && !condition(pc)) continue;
            string? option;
            if (profile.Targets.TryGetValue(t.Id, out var explicitOption)) option = explicitOption;
            else if (profile.AllRecommended)
                option = t.Risk == RiskLevel.Safe && t.Kind != TweakKind.Action ? SafeRecommendation(t, pc) : null;
            else if (t.Tags.Any(profile.IncludeTags.Contains) && t.Kind != TweakKind.Action)
                option = SafeRecommendation(t, pc);
            else option = null;
            if (option is null || t.GetOption(option) is null) continue;
            yield return (t, option);
        }
    }

    private static string? SafeRecommendation(TweakDefinition t, SystemProfile pc)
    {
        try { return t.RecommendationFor(pc); }
        catch (Exception ex)
        {
            Log.Warn("Profiles", $"recommandation de {t.Id} : {ex.Message}");
            return null;
        }
    }

    /// <summary>Nombre de réglages et d'applications cochés par défaut (affiché sur les cartes).</summary>
    public static (int Tweaks, int Apps) Count(ProfileDefinition profile, ModuleRegistry registry, SystemProfile pc) =>
        (Resolve(profile, registry, pc).Count(r => r.Tweak.Requirement.Check(pc) is null),
         profile.AppIds.Count(id => AppsCatalog.Find(id) is { } a && !(pc.Tier == PerformanceTier.Low && a.HasTag("heavy"))));

    public static async Task<ProfilePlan> BuildAsync(IReadOnlyList<ProfileDefinition> profiles, ImportedPlan? import, CancellationToken ct)
    {
        var registry = AppHost.Registry;
        var engine = AppHost.Engine;
        var pc = AppHost.Profile;
        var plan = new ProfilePlan { Profiles = profiles, Import = import };

        var categories = registry.Categories.Select((c, i) => (c.Id, c.Title, i)).ToDictionary(c => c.Id, c => (c.Title, c.i));
        var order = registry.Tweaks.Select((t, i) => (t.Id, i)).ToDictionary(x => x.Id, x => x.i, StringComparer.Ordinal);
        var rows = new Dictionary<string, PlanTweak>(StringComparer.Ordinal);

        PlanTweak Row(TweakDefinition t)
        {
            if (rows.TryGetValue(t.Id, out var row)) return row;
            var cat = categories.TryGetValue(t.Category, out var c) ? c : (t.Category, int.MaxValue);
            row = new PlanTweak { Tweak = t, CategoryTitle = cat.Item1, CategoryOrder = cat.Item2, Order = order.GetValueOrDefault(t.Id) };
            rows[t.Id] = row;
            return row;
        }

        await Task.Run(() =>
        {
            if (import is not null)
            {
                foreach (var (id, option) in import.Tweaks)
                {
                    if (registry.GetTweak(id) is not { } t || t.GetOption(option) is null) continue;
                    Row(t).Wants.Add(new TweakWant(ImportSource, "Fichier importé", option));
                }
            }
            else
            {
                foreach (var profile in profiles.OrderBy(p => ProfileCatalog.IndexOf(p.Id)))
                {
                    foreach (var (t, option) in Resolve(profile, registry, pc))
                    {
                        var row = Row(t);
                        row.Wants.Add(new TweakWant(profile.Id, profile.Title, option));
                        if (profile.OptIn.Contains(t.Id)) row.OptIn = true;
                    }
                }
            }

            foreach (var row in rows.Values)
            {
                // En cas de désaccord, le premier profil dans l'ordre du catalogue l'emporte (modifiable dans le plan).
                row.Target = row.Wants[0].Option;
                // Un réglage « à cocher » ne reste décoché que si aucun autre profil ne le veut sans réserve.
                if (row.OptIn && import is null)
                    row.OptIn = profiles.Where(p => row.Wants.Any(w => w.SourceId == p.Id)).All(p => p.OptIn.Contains(row.Tweak.Id));
                if (row.Tweak.Risk == RiskLevel.Advanced) row.OptIn = true;
                // Fichier non fiable : un réglage de sécurité dont l'option n'est ni la recommandation pour ce PC ni la cible
                // d'un profil du catalogue (SMBv1 réactivé, intégrité de la mémoire coupée sur un PC où elle n'a pas de
                // recommandation, SmartScreen désactivé…) n'est jamais coché d'office.
                if (import is not null && IsSecurityRelated(row.Tweak) &&
                    SafeRecommendation(row.Tweak, pc) != row.Target && !ProfileCatalog.IsKnownTarget(row.Tweak.Id, row.Target))
                {
                    row.OptIn = true;
                    row.LessSafe = true;
                }
                row.Unavailable = engine.Unavailability(row.Tweak);
            }
        }, ct).ConfigureAwait(false);

        // Détection de l'état actuel (lecture seule), 4 à la fois pour ménager les PC modestes.
        var available = rows.Values.Where(r => r.Unavailable is null).ToList();
        using (var gate = new SemaphoreSlim(4))
        {
            await Task.WhenAll(available.Select(async row =>
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try { row.Current = await engine.DetectAsync(row.Tweak).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    Log.Warn("Profiles", $"détection de {row.Tweak.Id} : {ex.Message}");
                    row.Current = TweakState.UnknownState;
                }
                finally { gate.Release(); }
            })).ConfigureAwait(false);
        }
        ct.ThrowIfCancellationRequested();

        foreach (var row in rows.Values.OrderBy(r => r.CategoryOrder).ThenBy(r => r.Order))
        {
            if (row.Unavailable is not null) { plan.Unavailable.Add(row); continue; }
            row.Selected = !row.AtTarget && !row.OptIn;
            plan.Tweaks.Add(row);
        }

        await BuildAppsAsync(plan, profiles, import, pc, ct).ConfigureAwait(false);

        if (import is not null)
        {
            plan.Notices.AddRange(import.Notices);
            var lessSafe = plan.Tweaks.Count(t => t.LessSafe && !t.AtTarget);
            if (lessSafe > 0)
                plan.Notices.Add($"{lessSafe} réglage{(lessSafe > 1 ? "s" : "")} de sécurité du fichier " +
                                 $"{(lessSafe > 1 ? "demandent" : "demande")} une option que Timonier ne propose pas pour ce PC : " +
                                 $"{(lessSafe > 1 ? "ils restent décochés" : "il reste décoché")}. Ne " +
                                 $"{(lessSafe > 1 ? "les" : "le")} cochez que si vous savez pourquoi.");
        }
        return plan;
    }

    private static bool IsSecurityRelated(TweakDefinition t) =>
        t.Category == "security" || t.Tags.Contains("security");

    private static async Task BuildAppsAsync(ProfilePlan plan, IReadOnlyList<ProfileDefinition> profiles, ImportedPlan? import, SystemProfile pc, CancellationToken ct)
    {
        var apps = new Dictionary<string, PlanApp>(StringComparer.OrdinalIgnoreCase);
        var low = pc.Tier == PerformanceTier.Low;
        var heavySkipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string id, bool preselect, string source)
        {
            var entry = AppsCatalog.Find(id);
            if (entry is not null && low && entry.HasTag("heavy"))
            {
                heavySkipped.Add(id);
                return;
            }
            if (!apps.TryGetValue(id, out var app))
            {
                app = new PlanApp
                {
                    Id = entry?.WingetId ?? id,
                    Name = entry?.Name ?? id,
                    Description = entry?.Description,
                    InCatalog = entry is not null,
                };
                apps[id] = app;
            }
            // Un identifiant hors du catalogue vérifié n'est jamais coché d'office.
            if (preselect && app.InCatalog) app.Preselected = true;
            if (!app.Sources.Contains(source)) app.Sources.Add(source);
        }

        if (import is not null)
        {
            foreach (var id in import.Apps) Add(id, true, "Fichier importé");
        }
        else
        {
            foreach (var p in profiles.OrderBy(p => ProfileCatalog.IndexOf(p.Id)))
            {
                foreach (var id in p.AppIds.Where(AppsCatalog.Contains)) Add(id, true, p.Title);
                foreach (var tag in p.AppTags)
                    foreach (var a in AppsCatalog.WithTag(tag)) Add(a.WingetId, false, p.Title);
                if (p.AllRecommended)
                    foreach (var a in AppsCatalog.All.Where(a => (a.Manufacturer is not null || a.GpuVendor is not null) && a.IsRelevantFor(pc)))
                        Add(a.WingetId, false, p.Title);
            }
        }
        plan.SkippedHeavyApps = heavySkipped.Count;

        // Programmes déjà installés (lecture du registre des programmes, sans élévation).
        try
        {
            var installed = await Task.Run(() => InstalledPrograms.Read(), ct).ConfigureAwait(false);
            foreach (var app in apps.Values)
            {
                if (AppsCatalog.Find(app.Id) is { } entry)
                    app.Installed = installed.Any(i => entry.MatchesInstalledName(i.DisplayName));
            }
            plan.InstalledAppsKnown = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn("Profiles", "liste des programmes installés : " + ex.Message);
        }

        foreach (var app in apps.Values.OrderByDescending(a => a.Preselected).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            app.Selected = app.Preselected && !app.Installed;
            plan.Apps.Add(app);
        }
    }
}
