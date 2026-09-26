using Microsoft.Win32;
using Timonier.Core.Platform;

namespace Timonier.Core.Model;

public enum TweakKind
{
    /// <summary>Interrupteur : options "on" et "off". "on" = la fonctionnalité Windows nommée par le titre est active.</summary>
    Toggle,
    /// <summary>Liste de choix exclusifs (clé -> opérations).</summary>
    Choice,
    /// <summary>Action ponctuelle (bouton). Option unique "run".</summary>
    Action,
}

public enum RiskLevel
{
    /// <summary>Sans danger, réversible.</summary>
    Safe,
    /// <summary>Peut dégrader une fonctionnalité ; à comprendre avant d'appliquer.</summary>
    Moderate,
    /// <summary>Réservé aux utilisateurs avertis (masqué hors « mode avancé »).</summary>
    Advanced,
}

[Flags]
public enum ApplyEffect
{
    None = 0,
    RestartExplorer = 1,
    SignOut = 2,
    Reboot = 4,
}

public sealed class TweakOption
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<Operation> Operations { get; init; } = [];
}

/// <summary>
/// Définition d'un réglage. Immuable, compilée dans l'application : c'est la seule source de vérité
/// pour ce que Timonier peut modifier (le broker admin refuse tout identifiant absent du catalogue).
/// </summary>
public sealed class TweakDefinition
{
    public const string On = "on";
    public const string Off = "off";
    public const string Run = "run";

    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public string? Group { get; init; }
    public TweakKind Kind { get; init; }
    public RiskLevel Risk { get; init; } = RiskLevel.Safe;
    public ApplyEffect Effect { get; init; }
    public Requirement Requirement { get; init; } = Requirement.None;
    public string[] Keywords { get; init; } = [];
    public string[] Tags { get; init; } = [];
    public string? Warning { get; init; }
    /// <summary>Option recommandée par Timonier (statique).</summary>
    public string? Recommended { get; init; }
    /// <summary>Recommandation adaptée au matériel/à l'édition (prioritaire sur <see cref="Recommended"/>).</summary>
    public Func<SystemProfile, string?>? AdaptiveRecommendation { get; init; }
    /// <summary>Option correspondant au comportement d'origine de Windows (utilisée si aucune option ne correspond exactement).</summary>
    public string? WindowsDefault { get; init; }
    public IReadOnlyList<TweakOption> Options { get; init; } = [];
    /// <summary>Détection personnalisée (lecture seule, processus non élevé). Retourne la clé d'option ou null si inconnue.</summary>
    public Func<string?>? CustomDetect { get; init; }
    /// <summary>Force le passage par le broker même si aucune opération ne l'exige.</summary>
    public bool ForceAdmin { get; init; }

    public bool RequiresAdmin => ForceAdmin || Options.Any(o => o.Operations.Any(op => op.RequiresAdmin));

    public TweakOption? GetOption(string key) => Options.FirstOrDefault(o => o.Key == key);

    public string? RecommendationFor(SystemProfile profile) => AdaptiveRecommendation?.Invoke(profile) ?? Recommended;

    /// <summary>Une action composée uniquement d'outils système n'est pas annulable automatiquement.</summary>
    public bool IsReversible => Kind != TweakKind.Action ||
        Options.SelectMany(o => o.Operations).All(op => op is not RunToolOp);

    public override string ToString() => Id;
}

/// <summary>Constructeur fluide pour déclarer les réglages de façon concise.</summary>
public static class Tweak
{
    public static TweakBuilder Toggle(string id, string title, string description) => new(id, title, description, TweakKind.Toggle);
    public static TweakBuilder Choice(string id, string title, string description) => new(id, title, description, TweakKind.Choice);
    public static TweakBuilder Action(string id, string title, string description) => new(id, title, description, TweakKind.Action);
}

public sealed class TweakBuilder
{
    private readonly string _id, _title, _description;
    private readonly TweakKind _kind;
    private string _category = "misc";
    private string? _group, _warning, _recommended, _default;
    private RiskLevel _risk = RiskLevel.Safe;
    private ApplyEffect _effect;
    private Requirement _requirement = Requirement.None;
    private string[] _keywords = [], _tags = [];
    private readonly List<TweakOption> _options = [];
    private Func<string?>? _detect;
    private Func<SystemProfile, string?>? _adaptive;
    private bool _forceAdmin;
    private string _onLabel = L("On"), _offLabel = L("Off");

    internal TweakBuilder(string id, string title, string description, TweakKind kind)
    {
        _id = id; _title = title; _description = description; _kind = kind;
    }

    public TweakBuilder In(string category, string? group = null) { _category = category; _group = group; return this; }
    public TweakBuilder Keywords(params string[] keywords) { _keywords = keywords; return this; }
    public TweakBuilder Tags(params string[] tags) { _tags = tags; return this; }
    public TweakBuilder Risk(RiskLevel risk) { _risk = risk; return this; }
    public TweakBuilder Effect(ApplyEffect effect) { _effect = effect; return this; }
    public TweakBuilder Requires(Requirement requirement) { _requirement = requirement; return this; }
    public TweakBuilder Warning(string warning) { _warning = warning; return this; }
    public TweakBuilder Recommend(string optionKey) { _recommended = optionKey; return this; }
    public TweakBuilder RecommendWhen(Func<SystemProfile, string?> adaptive) { _adaptive = adaptive; return this; }
    public TweakBuilder WindowsDefault(string optionKey) { _default = optionKey; return this; }
    public TweakBuilder Detect(Func<string?> detector) { _detect = detector; return this; }
    public TweakBuilder AlwaysAdmin() { _forceAdmin = true; return this; }
    /// <summary>Libellés des deux états d'un interrupteur (par défaut « Activé » / « Désactivé »).</summary>
    public TweakBuilder Labels(string onLabel, string offLabel) { _onLabel = onLabel; _offLabel = offLabel; return this; }

    public TweakBuilder WhenOn(params Operation[] ops) => AddOrReplace(TweakDefinition.On, _onLabel, null, ops);
    public TweakBuilder WhenOff(params Operation[] ops) => AddOrReplace(TweakDefinition.Off, _offLabel, null, ops);
    public TweakBuilder Option(string key, string label, params Operation[] ops) => AddOrReplace(key, label, null, ops);
    public TweakBuilder OptionWithHelp(string key, string label, string description, params Operation[] ops) => AddOrReplace(key, label, description, ops);
    public TweakBuilder Run(params Operation[] ops) => AddOrReplace(TweakDefinition.Run, L("Run"), null, ops);

    private TweakBuilder AddOrReplace(string key, string label, string? description, Operation[] ops)
    {
        _options.RemoveAll(o => o.Key == key);
        _options.Add(new TweakOption { Key = key, Label = label, Description = description, Operations = ops });
        return this;
    }

    public TweakDefinition Build()
    {
        if (_kind == TweakKind.Toggle && (_options.Count != 2 || _options.All(o => o.Key != TweakDefinition.On) || _options.All(o => o.Key != TweakDefinition.Off)))
            throw new InvalidOperationException($"Le réglage {_id} (Toggle) doit définir WhenOn et WhenOff.");
        if (_kind == TweakKind.Choice && _options.Count < 2)
            throw new InvalidOperationException($"Le réglage {_id} (Choice) doit définir au moins 2 options.");
        if (_kind == TweakKind.Action && (_options.Count != 1 || _options[0].Key != TweakDefinition.Run))
            throw new InvalidOperationException($"Le réglage {_id} (Action) doit définir Run(...).");

        // Libellés : appliqués aussi si Labels() est appelé après WhenOn/WhenOff.
        var options = _options.Select(o => o.Key switch
        {
            TweakDefinition.On when _kind == TweakKind.Toggle => new TweakOption { Key = o.Key, Label = _onLabel, Description = o.Description, Operations = o.Operations },
            TweakDefinition.Off when _kind == TweakKind.Toggle => new TweakOption { Key = o.Key, Label = _offLabel, Description = o.Description, Operations = o.Operations },
            _ => o,
        }).ToList();

        return new TweakDefinition
        {
            Id = _id,
            Title = _title,
            Description = _description,
            Category = _category,
            Group = _group,
            Kind = _kind,
            Risk = _risk,
            Effect = _effect,
            Requirement = _requirement,
            Keywords = _keywords,
            Tags = _tags,
            Warning = _warning,
            Recommended = _recommended,
            AdaptiveRecommendation = _adaptive,
            WindowsDefault = _default,
            Options = options,
            CustomDetect = _detect,
            ForceAdmin = _forceAdmin,
        };
    }
}

/// <summary>Raccourcis pour les opérations de registre.</summary>
public static class Reg
{
    public static RegSet CuDword(string key, string name, int value) => new(RegHive.CurrentUser, key, name, RegistryValueKind.DWord, value);
    public static RegSet LmDword(string key, string name, int value) => new(RegHive.LocalMachine, key, name, RegistryValueKind.DWord, value);
    public static RegSet DefDword(string key, string name, int value) => new(RegHive.DefaultUser, key, name, RegistryValueKind.DWord, value);
    public static RegSet CuString(string key, string name, string value) => new(RegHive.CurrentUser, key, name, RegistryValueKind.String, value);
    public static RegSet LmString(string key, string name, string value) => new(RegHive.LocalMachine, key, name, RegistryValueKind.String, value);
    public static RegSet DefString(string key, string name, string value) => new(RegHive.DefaultUser, key, name, RegistryValueKind.String, value);
    public static RegSet Cu(string key, string name, RegistryValueKind kind, object value) => new(RegHive.CurrentUser, key, name, kind, value);
    public static RegSet Lm(string key, string name, RegistryValueKind kind, object value) => new(RegHive.LocalMachine, key, name, kind, value);
    public static RegDeleteValue CuDel(string key, string name) => new(RegHive.CurrentUser, key, name);
    public static RegDeleteValue LmDel(string key, string name) => new(RegHive.LocalMachine, key, name);
    public static RegDeleteKey CuDelKey(string key) => new(RegHive.CurrentUser, key);
    public static RegDeleteKey LmDelKey(string key) => new(RegHive.LocalMachine, key);
}

/// <summary>Raccourcis pour services et tâches planifiées.</summary>
public static class Sys
{
    public static ServiceStartOp Service(string name, ServiceStartKind start, bool stopIfDisabled = true) => new(name, start, stopIfDisabled);
    public static ScheduledTaskOp DisableTask(string path) => new(path, false);
    public static ScheduledTaskOp EnableTask(string path) => new(path, true);
    public static RunToolOp Tool(SystemTool tool, bool admin, string explanation, params string[] args) => new(tool, args, admin, explanation);
    public static BroadcastSettingChangeOp Broadcast(string area = "ImmersiveColorSet") => new(area);
}
