using System.ServiceProcess;
using Timonier.Core.Catalog;
using Timonier.Core.Model;
using Timonier.Core.Security;

namespace Timonier.Modules.Startup;

// Actions du module Démarrage. Elles s'exécutent dans l'interface (portée utilisateur) ou dans le broker élevé
// (portée machine, services, tâches) : aucune référence à AppHost ni à l'interface ici. Chaque paramètre est
// revalidé dans ExecuteAsync et doit correspondre à un élément réellement présent dans l'énumération actuelle.

/// <summary>Base commune : activer/désactiver ou supprimer une application au démarrage, pour une portée donnée.</summary>
public abstract class StartupItemActionBase : IActionHandler
{
    public abstract string Id { get; }
    public abstract string Title { get; }
    protected abstract StartupScope Scope { get; }
    protected abstract string[] AllowedKinds { get; }
    public bool RequiresAdmin => Scope == StartupScope.Machine;

    public virtual void ValidateParameters(IReadOnlyDictionary<string, string> p) => Parse(p, null);

    protected (StartupKind Kind, string Name) Parse(IReadOnlyDictionary<string, string> p, string? userSid)
    {
        var scope = Validate.OneOf(p, "scope", "user", "machine");
        if (!string.Equals(scope, Scope == StartupScope.User ? "user" : "machine", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException(L("Scope not allowed for this action."));
        var kind = StartupInventory.ParseKind(Validate.OneOf(p, "kind", AllowedKinds));
        Validate.Required(p, "name", 400);
        // Valeur exacte (non rognée) : « Outil » et « Outil  » sont deux entrées Run distinctes, il ne faut pas agir sur l'autre.
        var name = p["name"];
        StartupInventory.EnsureExists(Scope, kind, name, userSid);
        return (kind, name);
    }

    public abstract Task<ActionResult> ExecuteAsync(ActionContext context, IReadOnlyDictionary<string, string> parameters);

    protected static string Label(string name) => name.Length > 60 ? name[..57] + "…" : name;
}

/// <summary>Active ou désactive une application au démarrage, exactement comme le Gestionnaire des tâches.</summary>
public abstract class SetStartupItemEnabledBase : StartupItemActionBase
{
    public override void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        base.ValidateParameters(p);
        Validate.OneOf(p, "enabled", "true", "false");
    }

    public override Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        // Pour la portée utilisateur, l'action n'est jamais élevée : HKCU est celui de l'utilisateur courant.
        var (kind, name) = Parse(p, ctx.Elevated ? ctx.UserSid : null);
        var enabled = Validate.OneOf(p, "enabled", "true", "false") == "true";
        var ops = StartupInventory.SetEnabledOps(Scope, kind, name, enabled, DateTime.UtcNow);
        var entry = ctx.ApplyJournaled(Id, L("Startup: “{0}”", Label(name)), enabled ? LC("feminine", "On") : L("Disabled"), ops);
        var msg = enabled
            ? L("“{0}” will run at the next sign-in.", Label(name))
            : L("“{0}” will no longer run at sign-in.", Label(name));
        return Task.FromResult(ActionResult.Ok(msg) with { JournalId = entry.Id });
    }
}

public sealed class SetStartupItemEnabledUserAction : SetStartupItemEnabledBase
{
    public override string Id => "startup.item.setenabled.user";
    public override string Title => L("Enable or disable a startup app (your account)");
    protected override StartupScope Scope => StartupScope.User;
    protected override string[] AllowedKinds => ["run", "folder", "packaged"];
}

public sealed class SetStartupItemEnabledAction : SetStartupItemEnabledBase
{
    public override string Id => "startup.item.setenabled";
    public override string Title => L("Enable or disable a startup app (all users)");
    protected override StartupScope Scope => StartupScope.Machine;
    protected override string[] AllowedKinds => ["run", "run32", "folder"];
}

/// <summary>Supprime une entrée Run (et son état StartupApproved) ; restaurable depuis le journal.</summary>
public abstract class DeleteStartupItemBase : StartupItemActionBase
{
    public override Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        var (kind, name) = Parse(p, ctx.Elevated ? ctx.UserSid : null);
        var ops = StartupInventory.DeleteOps(Scope, kind, name);
        var entry = ctx.ApplyJournaled(Id, L("Startup: removal of “{0}”", Label(name)), L("Deleted"), ops);
        return Task.FromResult(ActionResult.Ok(L("Entry “{0}” deleted. You can restore it from History.", Label(name)))
            with { JournalId = entry.Id });
    }
}

public sealed class DeleteStartupItemUserAction : DeleteStartupItemBase
{
    public override string Id => "startup.item.delete.user";
    public override string Title => L("Delete a startup entry (your account)");
    protected override StartupScope Scope => StartupScope.User;
    protected override string[] AllowedKinds => ["run"];
}

public sealed class DeleteStartupItemAction : DeleteStartupItemBase
{
    public override string Id => "startup.item.delete";
    public override string Title => L("Delete a startup entry (all users)");
    protected override StartupScope Scope => StartupScope.Machine;
    protected override string[] AllowedKinds => ["run", "run32"];
}

/// <summary>Change le type de démarrage d'un service (journalisé, donc annulable).</summary>
public sealed class SetServiceStartAction : IActionHandler
{
    public string Id => "startup.service.setstart";
    public string Title => L("Change a service's startup type");
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => Parse(p);

    private static (string ConfigName, ServiceStartKind Start) Parse(IReadOnlyDictionary<string, string> p)
    {
        var name = Validate.Required(p, "name", 256);
        var start = ServiceInventory.ParseStart(Validate.OneOf(p, "start", "auto", "delayed", "manual", "disabled"));
        var config = ServiceInventory.EnsureManageable(name, forStartType: true);
        return (config, start);
    }

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        var (name, start) = Parse(p);
        ctx.Progress?.Report(L("Service “{0}”…", name));
        var label = ServiceInventory.StartLabel(start);
        var entry = ctx.ApplyJournaled(Id, L("Service “{0}”: startup type", name), label, [new ServiceStartOp(name, start)]);
        var msg = start == ServiceStartKind.Disabled
            ? L("Service “{0}” disabled (and stopped if it was running).", name)
            : L("Service “{0}”: startup {1}.", name, label.ToLower(Culture));
        return Task.FromResult(ActionResult.Ok(msg) with { JournalId = entry.Id });
    }
}

/// <summary>Démarre, arrête ou redémarre un service (état d'exécution, non journalisé).</summary>
public sealed class ServiceControlAction : IActionHandler
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public string Id => "startup.service.control";
    public string Title => L("Start or stop a service");
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => Parse(p);

    private static (string Name, string Command) Parse(IReadOnlyDictionary<string, string> p)
    {
        var name = Validate.Required(p, "name", 256);
        var command = Validate.OneOf(p, "command", "start", "stop", "restart");
        ServiceInventory.EnsureManageable(name, forStartType: false);
        return (name, command);
    }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        var (name, command) = Parse(p);
        try
        {
            using var sc = new ServiceController(name);
            if (command is "stop" or "restart")
            {
                sc.Refresh();
                if (sc.Status != ServiceControllerStatus.Stopped)
                {
                    if (!sc.CanStop) return ActionResult.Fail(L("The service “{0}” can't be stopped.", name));
                    ctx.Progress?.Report(L("Stopping “{0}”…", name));
                    sc.Stop();
                    await Task.Run(() => sc.WaitForStatus(ServiceControllerStatus.Stopped, Timeout), ctx.Cancellation);
                }
                if (command == "stop") return ActionResult.Ok(L("Service “{0}” stopped.", name));
            }
            sc.Refresh();
            if (sc.Status == ServiceControllerStatus.Running) return ActionResult.Ok(L("The service “{0}” is already running.", name));
            if (Core.Platform.ServiceConfig.ReadStart(ServiceInventory.EnsureManageable(name, forStartType: true)) == ServiceStartKind.Disabled)
                return ActionResult.Fail(L("This service is disabled: choose another startup type first."));
            ctx.Progress?.Report(L("Starting “{0}”…", name));
            sc.Start();
            await Task.Run(() => sc.WaitForStatus(ServiceControllerStatus.Running, Timeout), ctx.Cancellation);
            return ActionResult.Ok(command == "restart" ? L("Service “{0}” restarted.", name) : L("Service “{0}” started.", name));
        }
        catch (System.ServiceProcess.TimeoutException)
        {
            return ActionResult.Fail(L("The service “{0}” didn't respond within {1:0} seconds.", name, Timeout.TotalSeconds));
        }
        catch (InvalidOperationException ex)
        {
            return ActionResult.Fail(L("Operation failed on “{0}”: {1}", name, (ex.InnerException ?? ex).Message));
        }
    }
}

/// <summary>Active ou désactive une tâche planifiée hors tâches système de Windows (journalisé, annulable).</summary>
public sealed class SetTaskEnabledAction : IActionHandler
{
    public string Id => "startup.task.setenabled";
    public string Title => L("Enable or disable a scheduled task");
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => Parse(p);

    private static (string Path, bool Enabled) Parse(IReadOnlyDictionary<string, string> p)
    {
        var path = Validate.Required(p, "path", 500);
        var enabled = Validate.OneOf(p, "enabled", "true", "false") == "true";
        TaskInventory.EnsureToggleable(path);
        return (path, enabled);
    }

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        var (path, enabled) = Parse(p);
        var entry = ctx.ApplyJournaled(Id, L("Scheduled task {0}", path), enabled ? LC("feminine", "On") : L("Disabled"), [new ScheduledTaskOp(path, enabled)]);
        var name = path[(path.LastIndexOf('\\') + 1)..];
        return Task.FromResult(ActionResult.Ok(enabled ? L("Task “{0}” enabled.", name) : L("Task “{0}” disabled.", name)) with { JournalId = entry.Id });
    }
}
