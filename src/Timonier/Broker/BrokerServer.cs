using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using Timonier.Core.Catalog;
using Timonier.Core.Engine;
using Timonier.Core.Platform;
using Timonier.Core.Security;

namespace Timonier.Broker;

/// <summary>
/// Processus élevé (lancé via UAC à la demande) qui exécute les opérations administrateur.
/// <list type="bullet">
/// <item>Vérifie que son client est bien Timonier.exe (même chemin) et le processus parent annoncé.</item>
/// <item>Canal nommé aléatoire, ACL limitée à l'utilisateur client, accès réseau refusé, une seule connexion.</item>
/// <item>N'exécute que des identifiants présents dans le catalogue compilé, paramètres revalidés ici.</item>
/// <item>Actions sensibles : confirmation affichée par ce processus élevé (non cliquable par un programme non élevé).</item>
/// <item>Se ferme après N minutes d'inactivité, à la fermeture de l'interface ou sur demande.</item>
/// </list>
/// </summary>
public static partial class BrokerServer
{
    [GeneratedRegex(@"^Timonier\.Broker\.[0-9a-f]{32}$")]
    private static partial Regex PipeNameRx();

    public static int Run(string[] args)
    {
        // Élevé : journal dans un dossier réservé aux administrateurs (jamais dans le profil, modifiable sans élévation).
        bool elevated;
        using (var me = WindowsIdentity.GetCurrent()) elevated = new WindowsPrincipal(me).IsInRole(WindowsBuiltInRole.Administrator);
        if (elevated) Log.UseProtectedFile("broker.log");
        else Log.UseFile("broker.log");
        try
        {
            return RunAsync(args).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Error("Broker", "arrêt sur erreur", ex);
            return 10;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        // --broker <tube> <pid parent> <minutes d'inactivité> [langue]
        if (args.Length is < 3 or > 4 || !PipeNameRx().IsMatch(args[0]) || !int.TryParse(args[1], out var parentPid) || parentPid <= 0
            || !int.TryParse(args[2], out var idleMinutes) || idleMinutes is < 1 or > 60
            || (args.Length == 4 && Core.Localization.Languages.Find(args[3]) is null))
        {
            Log.Warn("Broker", "arguments invalides");
            return 2;
        }
        // Défense en profondeur (l'interface vérifie déjà avant l'UAC) : pas de session si du code a pu être injecté.
        if (EnvironmentGuard.Find() is { Count: > 0 } injected)
        {
            Log.Warn("Broker", "variables d'environnement d'injection présentes, arrêt : " + string.Join(", ", injected));
            return 5;
        }
        // Confirmations et messages du broker dans la langue de l'interface qui l'a lancé.
        Core.Localization.Loc.Initialize(args.Length == 4 ? args[3] : null);

        using (var me = WindowsIdentity.GetCurrent())
        {
            if (!new WindowsPrincipal(me).IsInRole(WindowsBuiltInRole.Administrator) && !BrokerSelfTest.IsSelfTestBroker)
            {
                Log.Warn("Broker", "lancé sans élévation");
                return 3;
            }
        }

        // 1) Le parent doit être Timonier.exe, au même emplacement que nous.
        using var parent = Process.GetProcessById(parentPid);
        var parentPath = parent.MainModule?.FileName;
        if (!string.Equals(parentPath, AppPaths.ExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            Log.Warn("Broker", "processus parent non reconnu : " + parentPath);
            return 4;
        }
        var clientSid = Native.GetProcessUserSid(parent) ?? throw new InvalidOperationException("SID client introuvable");
        Log.Info("Broker", $"démarrage pour le client {parentPid}");

        // 2) Canal nommé protégé.
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(clientSid, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        using (var me = WindowsIdentity.GetCurrent())
            security.AddAccessRule(new PipeAccessRule(me.User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));

        await using var pipe = NamedPipeServerStreamAcl.Create(args[0], PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 0, 0, security);

        using (var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            try { await pipe.WaitForConnectionAsync(connectTimeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { Log.Warn("Broker", "aucun client"); return 5; }
        }

        // 3) Le client connecté doit être exactement le processus parent annoncé.
        if (!Native.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var clientPid) || clientPid != parentPid)
        {
            Log.Warn("Broker", $"client inattendu (pid {clientPid})");
            return 6;
        }

        var registry = ModuleRegistry.Build();
        var session = new Session(pipe, registry, clientSid.Value, TimeSpan.FromMinutes(idleMinutes));
        await session.RunAsync(parent).ConfigureAwait(false);
        Log.Info("Broker", "fin de session");
        return 0;
    }

    private sealed class Session(NamedPipeServerStream pipe, ModuleRegistry registry, string clientSid, TimeSpan idle)
    {
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        // Une seule opération administrateur à la fois : aucune action ne peut voir l'état modifié par une autre entre sa
        // vérification et son exécution, et les privilèges du jeton (SeBackup/SeRestore) ne sont jamais partagés.
        // La lecture du canal continue pendant ce temps, ce qui permet d'annuler l'opération en cours ou en attente.
        private readonly SemaphoreSlim _execGate = new(1, 1);
        private readonly ConcurrentDictionary<int, CancellationTokenSource> _running = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly Lock _undoGate = new();
        private DateTime _lastActivity = DateTime.UtcNow;

        public async Task RunAsync(Process parent)
        {
            _ = parent.WaitForExitAsync(_stop.Token).ContinueWith(_ => _stop.Cancel(), TaskScheduler.Default);
            _ = IdleWatchAsync();
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var frame = await BrokerFraming.ReadAsync(pipe, _stop.Token).ConfigureAwait(false);
                    if (frame is null) break;
                    _lastActivity = DateTime.UtcNow;
                    BrokerRequest? req;
                    try { req = JsonSerializer.Deserialize(frame, CoreJson.Default.BrokerRequest); }
                    catch (JsonException) { Log.Warn("Broker", "trame JSON invalide"); break; }
                    if (req is null) break;

                    if (req.Op == "cancel")
                    {
                        if (req.TargetId is { } target && _running.TryGetValue(target, out var cts)) cts.Cancel();
                        continue;
                    }
                    if (req.Op == "exit")
                    {
                        await SendAsync(new BrokerResponse { Id = req.Id, Ok = true, Message = L("Admin session closed.") }).ConfigureAwait(false);
                        break;
                    }
                    // Les requêtes longues (SFC, DISM, winget…) tournent en parallèle de la lecture pour pouvoir être annulées.
                    _ = Task.Run(() => HandleAsync(req));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Error("Broker", "boucle de lecture", ex); }
            finally
            {
                _stop.Cancel();
                foreach (var cts in _running.Values) cts.Cancel();
            }
        }

        private async Task IdleWatchAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), _stop.Token).ConfigureAwait(false);
                    if (_running.IsEmpty && DateTime.UtcNow - _lastActivity > idle)
                    {
                        Log.Info("Broker", "inactivité : fermeture");
                        _stop.Cancel();
                        pipe.Disconnect();
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Warn("Broker", "surveillance : " + ex.Message); }
        }

        private async Task HandleAsync(BrokerRequest req)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            _running[req.Id] = cts;
            BrokerResponse response;
            var entered = false;
            try
            {
                if (req.Op != "hello")
                {
                    await _execGate.WaitAsync(cts.Token).ConfigureAwait(false);
                    entered = true;
                }
                var progress = new FrameProgress(this, req.Id);
                var ctx = new ExecContext { Elevated = true, UserSid = clientSid, Progress = progress, Cancellation = cts.Token };
                response = req.Op switch
                {
                    "hello" => new BrokerResponse { Ok = true, ServerVersion = BrokerFraming.ProtocolVersion, Message = "Timonier broker" },
                    "apply" => Apply(req, ctx),
                    "action" => await RunActionAsync(req, ctx).ConfigureAwait(false),
                    "undo" => Undo(req, ctx),
                    _ => new BrokerResponse { Ok = false, Message = L("Unknown operation.") },
                };
            }
            catch (ValidationException ex) { response = new BrokerResponse { Ok = false, Message = ex.Message }; }
            catch (OperationCanceledException) { response = new BrokerResponse { Ok = false, Cancelled = true, Message = L("Operation canceled.") }; }
            catch (Exception ex)
            {
                Log.Error("Broker", $"requête {req.Op}", ex);
                response = new BrokerResponse { Ok = false, Message = TweakEngine.Friendly(ex) };
            }
            finally
            {
                if (entered) _execGate.Release();
                _running.TryRemove(req.Id, out _);
                _lastActivity = DateTime.UtcNow;
            }
            response.Id = req.Id;
            response.Final = true;
            await SendAsync(response).ConfigureAwait(false);
        }

        private BrokerResponse Apply(BrokerRequest req, ExecContext ctx)
        {
            var tweak = registry.GetTweak(req.TweakId ?? "") ?? throw new ValidationException(L("Unknown setting."));
            var option = tweak.GetOption(req.Option ?? "") ?? throw new ValidationException(L("Unknown option."));
            // Moindre privilège : un réglage qui ne demande pas l'admin ne s'exécute jamais dans le processus élevé.
            if (!tweak.RequiresAdmin) throw new ValidationException(L("This setting doesn't apply in administrator mode."));
            Log.Info("Broker", $"apply {tweak.Id}={option.Key}");
            var entry = TweakEngine.ApplyCore(tweak, option, null, ctx);
            return new BrokerResponse
            {
                Ok = true,
                Message = L("{0}: {1}", tweak.Title, option.Label),
                JournalId = entry.Id,
                Effect = (int)tweak.Effect,
            };
        }

        private async Task<BrokerResponse> RunActionAsync(BrokerRequest req, ExecContext ctx)
        {
            var handler = registry.GetAction(req.ActionId ?? "") ?? throw new ValidationException(L("Unknown action."));
            // Moindre privilège : une action qui ne demande pas l'admin ne s'exécute jamais dans le processus élevé.
            if (!handler.RequiresAdmin) throw new ValidationException(L("This action doesn't run in administrator mode."));
            var parameters = req.Params ?? [];
            Validate.ParameterBag(parameters);
            handler.ValidateParameters(parameters);
            if (handler.RequiresElevatedConfirmationFor(parameters) &&
                !Native.ConfirmFromElevatedProcess(L("Timonier — administrator confirmation"),
                    L("{0}\n\nThis confirmation is shown by Timonier's administrator process. Continue?", handler.DescribeForConfirmation(parameters))))
            {
                return new BrokerResponse { Ok = false, Cancelled = true, Message = L("Action declined at confirmation.") };
            }
            Log.Info("Broker", $"action {handler.Id}");
            var result = await handler.ExecuteAsync(new ActionContext { Exec = ctx }, parameters).ConfigureAwait(false);
            return new BrokerResponse
            {
                Ok = result.Success,
                Message = result.Message,
                Data = result.Data,
                JournalId = result.JournalId,
                Effect = (int)result.Effect,
            };
        }

        private BrokerResponse Undo(BrokerRequest req, ExecContext ctx)
        {
            // L'entrée est relue depuis HKLM (zone admin) : le client ne fournit que son identifiant.
            // Les requêtes tournent en parallèle : une même entrée ne doit jamais être annulée deux fois.
            JournalEntry entry;
            List<string> errors;
            lock (_undoGate)
            {
                entry = MachineJournalStore.Get(req.EntryId ?? Guid.Empty) ?? throw new ValidationException(L("History entry not found."));
                if (entry.Undone) throw new ValidationException(L("Already undone."));
                if (entry.UserSid is not null && entry.UserSid != clientSid)
                    throw new ValidationException(L("This change was made for another user account."));
                Log.Info("Broker", $"undo {entry.SourceId}");
                errors = OperationExecutor.Undo(entry.Undo, ctx);
                entry.Undone = true;
                entry.UndoneAt = DateTimeOffset.Now;
                if (errors.Count > 0) entry.Note = string.Join(" ; ", errors);
                MachineJournalStore.Write(entry);
            }
            var effect = registry.GetTweak(entry.SourceId)?.Effect ?? Core.Model.ApplyEffect.None;
            return errors.Count == 0
                ? new BrokerResponse { Ok = true, Message = L("Undone: {0}", entry.Title), Effect = (int)effect }
                : new BrokerResponse { Ok = false, Message = L("Partially undone: {0}", string.Join(" ; ", errors)) };
        }

        public async Task SendAsync(BrokerResponse response)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(response, CoreJson.Default.BrokerResponse);
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (pipe.IsConnected) await BrokerFraming.WriteAsync(pipe, bytes, CancellationToken.None).ConfigureAwait(false);
            }
            catch (IOException) { /* client parti */ }
            finally { _writeLock.Release(); }
        }

        private sealed class FrameProgress(Session session, int id) : IProgress<string>
        {
            private long _lastTicks;

            public void Report(string value)
            {
                // Limite à ~20 messages/s pour ne pas saturer le canal.
                var now = Environment.TickCount64;
                if (now - Interlocked.Read(ref _lastTicks) < 50) return;
                Interlocked.Exchange(ref _lastTicks, now);
                session._lastActivity = DateTime.UtcNow;
                _ = session.SendAsync(new BrokerResponse { Id = id, Final = false, Ok = true, Progress = value.Length > 500 ? value[..500] : value });
            }
        }
    }
}
