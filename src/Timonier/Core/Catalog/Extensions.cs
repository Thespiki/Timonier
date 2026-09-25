namespace Timonier.Core.Catalog;

public enum HealthStatus { Good, Info, Warning, Critical, Unknown }

public sealed record HealthResult(HealthStatus Status, string Summary, string? Detail = null);

/// <summary>
/// Contrôle de santé affiché sur le tableau de bord (ex. « Antivirus à jour », « Espace disque », « Usure batterie »).
/// Doit être en LECTURE SEULE, rapide (&lt; 2 s) et sans élévation. Exécuté hors du thread UI.
/// </summary>
public interface IHealthCheck
{
    string Id { get; }
    string Title { get; }
    string Glyph { get; }
    /// <summary>Page qui permet de corriger le problème (optionnel).</summary>
    string? PageId { get; }
    Task<HealthResult> CheckAsync(CancellationToken ct);
}

/// <summary>Implémentation prête à l'emploi d'un contrôle de santé.</summary>
public sealed class HealthCheck(string id, string title, string glyph, string? pageId, Func<CancellationToken, Task<HealthResult>> check) : IHealthCheck
{
    public string Id { get; } = id;
    public string Title { get; } = title;
    public string Glyph { get; } = glyph;
    public string? PageId { get; } = pageId;
    public Task<HealthResult> CheckAsync(CancellationToken ct) => check(ct);

    /// <summary>Version synchrone (exécutée sur le pool de threads).</summary>
    public static HealthCheck Sync(string id, string title, string glyph, string? pageId, Func<HealthResult> check) =>
        new(id, title, glyph, pageId, ct => Task.Run(check, ct));
}

/// <summary>
/// Action rapide proposée sur le tableau de bord et dans la recherche (ex. « Vider le cache DNS »).
/// <see cref="Execute"/> est appelé sur le thread UI (utiliser AppHost.Engine/Toasts/Dialogs dedans).
/// </summary>
public sealed record QuickAction(string Id, string Title, string Glyph, string Description, Func<Task> Execute)
{
    public string[] Keywords { get; init; } = [];
    /// <summary>Ordre d'affichage sur le tableau de bord (plus petit = plus visible).</summary>
    public int Order { get; init; } = 100;
    public bool RequiresAdmin { get; init; }
}
