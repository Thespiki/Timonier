using System.Windows;
using Timonier.Broker;
using Timonier.Core.Catalog;
using Timonier.Core.Engine;
using Timonier.Core.Model;
using Timonier.Core.Platform;
using Timonier.Core.Search;
using Timonier.Core.Settings;
using Timonier.UI.Services;

namespace Timonier;

/// <summary>
/// Accès centralisé aux services de l'application (interface uniquement : le broker ne l'utilise pas).
/// </summary>
public static class AppHost
{
    public static SystemProfile Profile { get; private set; } = null!;
    public static ModuleRegistry Registry { get; private set; } = null!;
    public static BrokerClient Broker { get; private set; } = null!;
    public static TweakEngine Engine { get; private set; } = null!;
    public static BackgroundRegistry Background { get; } = new();
    public static INavigator Navigator { get; set; } = null!;
    public static IDialogService Dialogs { get; set; } = null!;
    public static IToastService Toasts { get; set; } = null!;
    public static AppSettings Settings => SettingsStore.Current;

    /// <summary>Déclenché (thread UI) quand la détection matérielle est terminée.</summary>
    public static event EventHandler? HardwareLoaded;

    private static SearchEngine? _search;
    private static Task<SearchEngine>? _searchBuild;

    public static void Initialize()
    {
        Profile = SystemProfileService.LoadFast();
        Registry = ModuleRegistry.Build();
        Broker = new BrokerClient { IdleMinutes = Settings.BrokerIdleMinutes };
        Engine = new TweakEngine(Registry, Broker, Profile);
        Engine.Changed += (_, _) => { };

        _ = Task.Run(() =>
        {
            SystemProfileService.LoadHardware(Profile);
            Application.Current?.Dispatcher.InvokeAsync(() => HardwareLoaded?.Invoke(null, EventArgs.Empty));
        });
        // L'index de recherche est construit en arrière-plan, sans bloquer l'affichage.
        _searchBuild = Task.Run(BuildSearch);
    }

    public static async Task<SearchEngine> GetSearchAsync()
    {
        if (_search is not null) return _search;
        _search = await (_searchBuild ??= Task.Run(BuildSearch));
        return _search;
    }

    /// <summary>Reconstruit l'index (après ajout dynamique d'entrées par un module).</summary>
    public static void InvalidateSearch()
    {
        _search = null;
        _searchBuild = Task.Run(BuildSearch);
    }

    private static SearchEngine BuildSearch()
    {
        var docs = new List<SearchDocument>();
        foreach (var t in Registry.Tweaks)
        {
            var category = Registry.GetCategory(t.Category);
            docs.Add(SearchDocument.Create("tweak:" + t.Id, t.Title,
                (category?.Title ?? t.Category) + (t.Group is null ? "" : " › " + t.Group),
                category?.Glyph ?? "", SearchEntryKind.Tweak, t,
                t.Keywords.Concat(t.Tags), t.Description, category?.Title, 0, t.Kind == TweakKind.Toggle));
        }
        foreach (var p in Registry.Pages)
            docs.Add(SearchDocument.Create("page:" + p.Id, p.Title, p.Description ?? "Page", p.Glyph, SearchEntryKind.Page, p, p.Keywords, p.Description, null, 0.15));
        foreach (var c in Registry.Categories.Where(c => Registry.Pages.All(p => p.CategoryId != c.Id)))
            docs.Add(SearchDocument.Create("category:" + c.Id, c.Title, c.Description, c.Glyph, SearchEntryKind.Page, c, null, c.Description, null, 0.15));
        foreach (var e in Registry.SearchEntries)
            docs.Add(SearchDocument.Create("entry:" + e.Id, e.Title, e.Subtitle, e.Glyph, e.Kind, e, e.Keywords, null, null, e.Boost));
        return new SearchEngine(docs);
    }

    /// <summary>Ferme proprement la session admin et libère les ressources.</summary>
    public static async Task ShutdownAsync()
    {
        try { await Broker.StopAsync(); } catch { /* ignoré */ }
    }
}
