using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using Timonier.Core.Engine;
using Timonier.Core.Model;
using Timonier.Core.Platform;

namespace Timonier.Broker;

/// <summary>
/// Côté interface : démarre le broker à la demande (une invite UAC), vérifie son identité, envoie les requêtes.
/// </summary>
public sealed class BrokerClient : IAsyncDisposable
{
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<int, Pending> _pending = new();
    private NamedPipeClientStream? _pipe;
    private Process? _process;
    private int _nextId;

    private sealed record Pending(TaskCompletionSource<BrokerResponse> Completion, IProgress<string>? Progress);

    /// <summary>Déclenché quand la session admin s'ouvre ou se ferme (thread quelconque).</summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Appelé juste avant d'afficher l'invite UAC : l'interface peut expliquer pourquoi (ou refuser).
    /// Retourner false annule l'opération.
    /// </summary>
    public Func<Task<bool>>? BeforeElevation { get; set; }

    public int IdleMinutes { get; set; } = 5;

    /// <summary>Auto-test (builds Debug uniquement) : lance le broker SANS élévation pour vérifier le canal.</summary>
    internal bool SelfTestWithoutElevation { get; init; }

    /// <summary>PID du broker en cours (diagnostic).</summary>
    internal int? BrokerProcessId => _process?.Id;

    /// <summary>Envoie une requête brute (auto-test).</summary>
    internal Task<BrokerResponse> SendRawAsync(BrokerRequest request, CancellationToken ct) => SendAsync(request, null, ct);
    public bool IsRunning => _pipe?.IsConnected == true;
    public DateTime? StartedAt { get; private set; }

    public async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (IsRunning) return;
        await _connectLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (IsRunning) return;
            // Variables permettant d'injecter du code dans le futur processus administrateur : on n'élève pas.
            if (EnvironmentGuard.Find() is { Count: > 0 } injected)
            {
                Log.Warn("Broker", "élévation refusée, variables d'environnement : " + string.Join(", ", injected));
                throw new InvalidOperationException(
                    "Session administrateur refusée par sécurité : ces variables d'environnement permettraient à un autre programme " +
                    "d'exécuter son code avec les droits administrateur de Timonier : " + string.Join(", ", injected) + ". " +
                    "Si vous ne les avez pas créées vous-même (outil de profilage .NET), supprimez-les dans « Modifier les variables " +
                    "d'environnement » et faites analyser le PC par votre antivirus.");
            }
            if (BeforeElevation is not null && !await BeforeElevation().ConfigureAwait(false))
                throw new OperationCanceledException("Opération annulée.");

            var pipeName = "Timonier.Broker." + Guid.NewGuid().ToString("N");
            Process process;
            try
            {
                // Arguments construits uniquement à partir d'un GUID et d'entiers : aucune donnée externe.
                var arguments = $"--broker {pipeName} {Environment.ProcessId} {Math.Clamp(IdleMinutes, 1, 60)} {Core.Localization.Loc.Language}";
                var psi = new ProcessStartInfo(AppPaths.ExecutablePath)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = arguments,
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                if (SelfTestWithoutElevation) psi = BrokerSelfTest.UnelevatedStartInfo(arguments);
                process = Process.Start(psi) ?? throw new InvalidOperationException("Impossible de démarrer la session administrateur.");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                throw new OperationCanceledException("Autorisation administrateur refusée (UAC).");
            }

            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(30_000, ct).ConfigureAwait(false);
                // Le serveur doit être le processus que nous venons de lancer (anti-usurpation du canal).
                if (!Native.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverPid) || serverPid != process.Id)
                    throw new InvalidOperationException("Le canal administrateur n'appartient pas au processus attendu : connexion refusée.");
            }
            catch
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                process.Dispose();
                throw;
            }

            _pipe = pipe;
            _process = process;
            StartedAt = DateTime.Now;
            _ = ReadLoopAsync(pipe);

            try
            {
                var hello = await SendCoreAsync(new BrokerRequest { Op = "hello" }, null, ct).ConfigureAwait(false);
                if (hello.ServerVersion != BrokerFraming.ProtocolVersion)
                    throw new InvalidOperationException("Version du broker incompatible.");
            }
            catch
            {
                // Poignée de main ratée : on ne garde pas un canal « connecté » vers un broker inutilisable.
                await DisconnectAsync().ConfigureAwait(false);
                throw;
            }
            Log.Info("BrokerClient", "session admin ouverte");
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async Task<ApplyOutcome> ApplyTweakAsync(string tweakId, string option, CancellationToken ct)
    {
        var r = await SendAsync(new BrokerRequest { Op = "apply", TweakId = tweakId, Option = option }, null, ct).ConfigureAwait(false);
        return ToOutcome(r);
    }

    public async Task<ApplyOutcome> RunActionAsync(string actionId, IReadOnlyDictionary<string, string> parameters, IProgress<string>? progress, CancellationToken ct)
    {
        var r = await SendAsync(new BrokerRequest { Op = "action", ActionId = actionId, Params = new Dictionary<string, string>(parameters) }, progress, ct).ConfigureAwait(false);
        return ToOutcome(r);
    }

    public async Task<ApplyOutcome> UndoAsync(Guid entryId, CancellationToken ct)
    {
        var r = await SendAsync(new BrokerRequest { Op = "undo", EntryId = entryId }, null, ct).ConfigureAwait(false);
        return ToOutcome(r);
    }

    /// <summary>Ferme la session administrateur (le broker se termine).</summary>
    public async Task StopAsync()
    {
        if (!IsRunning) return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await SendCoreAsync(new BrokerRequest { Op = "exit" }, null, cts.Token).ConfigureAwait(false);
        }
        catch { /* déjà fermé */ }
        await DisconnectAsync().ConfigureAwait(false);
    }

    private static ApplyOutcome ToOutcome(BrokerResponse r) =>
        new(r.Ok, r.Message ?? (r.Ok ? "Terminé." : "Échec."), (ApplyEffect)r.Effect, r.JournalId) { Data = r.Data, Cancelled = r.Cancelled };

    private async Task<BrokerResponse> SendAsync(BrokerRequest request, IProgress<string>? progress, CancellationToken ct)
    {
        await EnsureConnectedAsync(ct).ConfigureAwait(false);
        return await SendCoreAsync(request, progress, ct).ConfigureAwait(false);
    }

    private async Task<BrokerResponse> SendCoreAsync(BrokerRequest request, IProgress<string>? progress, CancellationToken ct)
    {
        var pipe = _pipe ?? throw new IOException("Session administrateur fermée.");
        request.Id = Interlocked.Increment(ref _nextId);
        var pending = new Pending(new TaskCompletionSource<BrokerResponse>(TaskCreationOptions.RunContinuationsAsynchronously), progress);
        _pending[request.Id] = pending;
        try
        {
            await WriteAsync(pipe, request).ConfigureAwait(false);
            using (ct.Register(() => _ = WriteAsync(pipe, new BrokerRequest { Op = "cancel", TargetId = request.Id })))
            {
                return await pending.Completion.Task.ConfigureAwait(false);
            }
        }
        finally
        {
            _pending.TryRemove(request.Id, out _);
        }
    }

    private async Task WriteAsync(NamedPipeClientStream pipe, BrokerRequest request)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request, Core.Platform.CoreJson.Default.BrokerRequest);
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try { await BrokerFraming.WriteAsync(pipe, bytes, CancellationToken.None).ConfigureAwait(false); }
        finally { _writeLock.Release(); }
    }

    private async Task ReadLoopAsync(NamedPipeClientStream pipe)
    {
        try
        {
            while (true)
            {
                var frame = await BrokerFraming.ReadAsync(pipe, CancellationToken.None).ConfigureAwait(false);
                if (frame is null) break;
                var response = JsonSerializer.Deserialize(frame, Core.Platform.CoreJson.Default.BrokerResponse);
                if (response is null || !_pending.TryGetValue(response.Id, out var pending)) continue;
                if (!response.Final)
                {
                    if (response.Progress is { } text) pending.Progress?.Report(text);
                    continue;
                }
                pending.Completion.TrySetResult(response);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidDataException or JsonException)
        {
            Log.Info("BrokerClient", "canal fermé : " + ex.Message);
        }
        finally
        {
            foreach (var p in _pending.Values)
                p.Completion.TrySetException(new IOException("La session administrateur s'est fermée."));
            if (ReferenceEquals(_pipe, pipe)) await DisconnectAsync().ConfigureAwait(false);
        }
    }

    private async Task DisconnectAsync()
    {
        var pipe = Interlocked.Exchange(ref _pipe, null);
        var process = Interlocked.Exchange(ref _process, null);
        if (pipe is null) return;
        StartedAt = null;
        try { await pipe.DisposeAsync().ConfigureAwait(false); } catch { }
        process?.Dispose();
        Log.Info("BrokerClient", "session admin fermée");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
