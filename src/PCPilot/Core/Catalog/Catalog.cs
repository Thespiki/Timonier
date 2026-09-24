using System.Reflection;
using System.Windows;
using PcPilot.Core.Engine;
using PcPilot.Core.Model;
using PcPilot.Core.Platform;

namespace PcPilot.Core.Catalog;

/// <summary>
/// Un module fonctionnel (Confidentialité, Périphériques, Kiosque…). Découvert automatiquement par réflexion :
/// il suffit de créer une classe publique non abstraite qui implémente cette interface avec un constructeur sans paramètre.
/// <para><see cref="Register"/> est appelé AUSSI dans le broker élevé (sans interface graphique) : il ne doit
/// rien créer d'UI, ni faire d'accès lent (réseau, WMI…). Les pages sont des fabriques (lambdas) évaluées plus tard.</para>
/// </summary>
public interface IModule
{
    void Register(ModuleRegistry registry);
}

/// <summary>Sections de la navigation latérale.</summary>
public enum NavSection
{
    Overview = 0,
    Settings = 1,
    Control = 2,
    Tools = 3,
    App = 4,
}

/// <summary>Catégorie de réglages ; sans page personnalisée, une page générique liste ses réglages.</summary>
public sealed record CategoryInfo(string Id, string Title, string Glyph, string Description);

/// <summary>Entrée de navigation. <paramref name="Factory"/> est appelée à la première visite (chargement paresseux).</summary>
public sealed record PageInfo(string Id, string Title, string Glyph, NavSection Section, int Order, Func<FrameworkElement> Factory)
{
    public string? Description { get; init; }
    public string[] Keywords { get; init; } = [];
    /// <summary>Catégorie de réglages affichée par cette page (permet de rediriger la recherche vers la bonne page).</summary>
    public string? CategoryId { get; init; }
}

public enum SearchEntryKind { Page, Tweak, Feature, WindowsSetting, Tool }

/// <summary>Élément indexé par la recherche en plus des pages et réglages (fonction d'une page, lien Windows, outil…).</summary>
public sealed class SearchEntry
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string Glyph { get; init; } = "";
    public string[] Keywords { get; init; } = [];
    public SearchEntryKind Kind { get; init; } = SearchEntryKind.Feature;
    /// <summary>Page à ouvrir (optionnel) et paramètre de navigation (ex. ancre).</summary>
    public string? PageId { get; init; }
    public object? PageParameter { get; init; }
    /// <summary>Action directe (ex. ouvrir un outil Windows). Exécutée sur le thread UI.</summary>
    public Action? Execute { get; init; }
    /// <summary>Bonus de pertinence (0 = neutre). Les éléments fréquents peuvent être légèrement favorisés.</summary>
    public double Boost { get; init; }
}

/// <summary>Résultat d'une action paramétrée.</summary>
public sealed record ActionResult(bool Success, string Message)
{
    public Dictionary<string, string>? Data { get; init; }
    public Guid? JournalId { get; init; }
    public ApplyEffect Effect { get; init; }

    public static ActionResult Ok(string message, Dictionary<string, string>? data = null) => new(true, message) { Data = data };
    public static ActionResult Fail(string message) => new(false, message);
}

public sealed class ActionContext
{
    public required ExecContext Exec { get; init; }
    public IProgress<string>? Progress => Exec.Progress;
    public CancellationToken Cancellation => Exec.Cancellation;
    public bool Elevated => Exec.Elevated;
    public string? UserSid => Exec.UserSid;

    /// <summary>Exécute une liste d'opérations déclaratives et enregistre une entrée de journal annulable.</summary>
    public JournalEntry ApplyJournaled(string sourceId, string title, string toLabel, IEnumerable<Operation> ops)
    {
        var entry = new JournalEntry { SourceId = sourceId, Title = title, ToLabel = toLabel, Machine = Exec.Elevated, UserSid = Exec.UserSid };
        try
        {
            foreach (var op in ops) entry.Undo.Add(OperationExecutor.Execute(op, Exec));
        }
        catch
        {
            // Échec partiel : on remet en l'état ce qui a déjà été modifié.
            OperationExecutor.Undo(entry.Undo, Exec);
            throw;
        }
        JournalWriter.Write(entry, Exec.Elevated);
        return entry;
    }
}

/// <summary>
/// Action paramétrée (ex. « changer le DNS », « désactiver le périphérique X », « installer l'app Y »).
/// Les paramètres sont TOUJOURS des chaînes, validés par <see cref="Validate"/> avant exécution, dans le broker
/// comme dans l'interface. Ne jamais les concaténer dans une ligne de commande : utiliser ArgumentList,
/// des API Win32/WMI/WinRT, ou <see cref="PowerShellRunner"/> (variables d'environnement).
/// </summary>
public interface IActionHandler
{
    string Id { get; }
    string Title { get; }
    bool RequiresAdmin { get; }
    /// <summary>
    /// Action sensible (création d'admin, installation de logiciel, ouverture de session automatique…) :
    /// le broker affiche sa propre confirmation, non cliquable par un programme non élevé.
    /// </summary>
    bool RequiresElevatedConfirmation => false;
    /// <summary>
    /// Variante qui dépend des paramètres (déjà validés), ex. installer un logiciel hors du catalogue vérifié.
    /// C'est elle que le broker consulte ; par défaut, elle reprend <see cref="RequiresElevatedConfirmation"/>.
    /// </summary>
    bool RequiresElevatedConfirmationFor(IReadOnlyDictionary<string, string> parameters) => RequiresElevatedConfirmation;
    /// <summary>Texte affiché dans la confirmation élevée (paramètres déjà validés).</summary>
    string DescribeForConfirmation(IReadOnlyDictionary<string, string> parameters) => Title;
    /// <summary>Valide les paramètres ; lève <see cref="Security.ValidationException"/> avec un message clair.</summary>
    void ValidateParameters(IReadOnlyDictionary<string, string> parameters);
    Task<ActionResult> ExecuteAsync(ActionContext context, IReadOnlyDictionary<string, string> parameters);
}

/// <summary>Registre peuplé par les modules. Figé après le démarrage.</summary>
public sealed class ModuleRegistry
{
    private readonly Dictionary<string, TweakDefinition> _tweaks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IActionHandler> _actions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CategoryInfo> _categories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PageInfo> _pages = new(StringComparer.Ordinal);
    private readonly List<SearchEntry> _search = [];
    private readonly List<string> _errors = [];
    private readonly Dictionary<string, IHealthCheck> _health = new(StringComparer.Ordinal);
    private readonly Dictionary<string, QuickAction> _quick = new(StringComparer.Ordinal);

    public IReadOnlyCollection<IHealthCheck> HealthChecks => _health.Values;
    public IReadOnlyCollection<QuickAction> QuickActions => _quick.Values;

    public void AddHealthCheck(IHealthCheck check)
    {
        if (!_health.TryAdd(check.Id, check)) _errors.Add($"Contrôle de santé en double : {check.Id}");
    }

    /// <summary>Ajoute une action rapide (tableau de bord) ; elle est aussi indexée par la recherche.</summary>
    public void AddQuickAction(QuickAction action)
    {
        if (!_quick.TryAdd(action.Id, action)) { _errors.Add($"Action rapide en double : {action.Id}"); return; }
        _search.Add(new SearchEntry
        {
            Id = "quick:" + action.Id,
            Title = action.Title,
            Subtitle = action.Description,
            Glyph = action.Glyph,
            Keywords = action.Keywords,
            Kind = SearchEntryKind.Feature,
            Execute = () => _ = action.Execute(),
        });
    }

    public IReadOnlyCollection<TweakDefinition> Tweaks => _tweaks.Values;
    public IReadOnlyCollection<IActionHandler> Actions => _actions.Values;
    public IReadOnlyCollection<CategoryInfo> Categories => _categories.Values;
    public IReadOnlyCollection<PageInfo> Pages => _pages.Values;
    public IReadOnlyList<SearchEntry> SearchEntries => _search;
    /// <summary>Incohérences détectées au chargement (doublons…) : affichées dans la page Transparence.</summary>
    public IReadOnlyList<string> Errors => _errors;

    public void AddCategory(CategoryInfo category)
    {
        if (!_categories.TryAdd(category.Id, category)) _errors.Add($"Catégorie en double : {category.Id}");
    }

    public void AddTweak(TweakDefinition tweak)
    {
        if (!_tweaks.TryAdd(tweak.Id, tweak)) _errors.Add($"Réglage en double : {tweak.Id}");
    }

    public void AddTweaks(IEnumerable<TweakDefinition> tweaks)
    {
        foreach (var t in tweaks) AddTweak(t);
    }

    public void AddAction(IActionHandler handler)
    {
        if (!_actions.TryAdd(handler.Id, handler)) _errors.Add($"Action en double : {handler.Id}");
    }

    public void AddPage(PageInfo page)
    {
        if (!_pages.TryAdd(page.Id, page)) _errors.Add($"Page en double : {page.Id}");
    }

    public void AddSearchEntry(SearchEntry entry) => _search.Add(entry);

    public void AddSearchEntries(IEnumerable<SearchEntry> entries) => _search.AddRange(entries);

    public TweakDefinition? GetTweak(string id) => _tweaks.GetValueOrDefault(id);
    public IActionHandler? GetAction(string id) => _actions.GetValueOrDefault(id);
    public CategoryInfo? GetCategory(string id) => _categories.GetValueOrDefault(id);
    public PageInfo? GetPage(string id) => _pages.GetValueOrDefault(id);

    public IEnumerable<TweakDefinition> TweaksIn(string categoryId) => _tweaks.Values.Where(t => t.Category == categoryId);

    /// <summary>Page qui affiche une catégorie donnée (page dédiée si elle existe, sinon page générique).</summary>
    public string PageIdForCategory(string categoryId) =>
        _pages.Values.FirstOrDefault(p => p.CategoryId == categoryId)?.Id ?? "category:" + categoryId;

    /// <summary>Découvre et enregistre tous les modules de l'assembly.</summary>
    public static ModuleRegistry Build()
    {
        var registry = new ModuleRegistry();
        var moduleTypes = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IModule).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(t => t.FullName, StringComparer.Ordinal);
        foreach (var type in moduleTypes)
        {
            try
            {
                ((IModule)Activator.CreateInstance(type)!).Register(registry);
            }
            catch (Exception ex)
            {
                registry._errors.Add($"Module {type.Name} : {ex.Message}");
                Log.Error("Catalog", "module " + type.Name, ex);
            }
        }
        registry.ValidateConsistency();
        return registry;
    }

    private void ValidateConsistency()
    {
        foreach (var t in _tweaks.Values)
        {
            if (!_categories.ContainsKey(t.Category)) _errors.Add($"{t.Id} : catégorie inconnue « {t.Category} »");
            if (t.Recommended is not null && t.GetOption(t.Recommended) is null) _errors.Add($"{t.Id} : option recommandée inconnue");
            if (t.WindowsDefault is not null && t.GetOption(t.WindowsDefault) is null) _errors.Add($"{t.Id} : option par défaut inconnue");
        }
    }
}
