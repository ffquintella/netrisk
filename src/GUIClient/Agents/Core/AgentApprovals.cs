using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace GUIClient.Agents;

/// <summary>
/// Asks the user. The GUI implements it with the two approval dialogs; tests answer directly. A cancelled token means
/// the request stopped waiting (expired, server stopped) and the dialog must close without a decision.
/// </summary>
public interface IAgentApprovalPrompter
{
    Task<bool> ConfirmRegistrationAsync(RegistrationPrompt prompt, CancellationToken cancellationToken);

    Task<AccessDecision> ConfirmAccessAsync(AccessPrompt prompt, CancellationToken cancellationToken);
}

/// <summary>
/// The approval dialogs come forward on their own, possibly while the user is typing elsewhere: Deny has the focus,
/// and the approving button stays disabled for <see cref="ArmDelay"/> after the dialog opens, so a keystroke meant
/// for another window cannot approve.
/// </summary>
public static class AgentApprovalTiming
{
    public static readonly TimeSpan ArmDelay = TimeSpan.FromSeconds(1);
}

/// <summary>
/// Puts approval requests in front of the user one at a time (T346 "request collapsing"): whatever arrives while a
/// dialog is open waits in the queue and is counted in that dialog, so a flood is never a stack of dialogs.
/// </summary>
public sealed class AgentApprovalCoordinator : IAsyncDisposable
{
    private readonly AgentRegistry _registry;
    private readonly AgentLeaseManager _leases;
    private readonly IAgentApprovalPrompter _prompter;
    private readonly IAgentAuditSink _audit;
    private readonly TimeProvider _time;
    private Channel<(bool IsRegistration, string Id)> _queue = Channel.CreateUnbounded<(bool, string)>();
    private CancellationTokenSource? _stop;
    private Task? _worker;

    public AgentApprovalCoordinator(AgentRegistry registry, AgentLeaseManager leases, IAgentApprovalPrompter prompter,
        IAgentAuditSink audit, TimeProvider time)
    {
        _registry = registry;
        _leases = leases;
        _prompter = prompter;
        _audit = audit;
        _time = time;
    }

    public bool IsRunning => _worker is { IsCompleted: false };

    public void Start()
    {
        if (IsRunning) return;
        _queue = Channel.CreateUnbounded<(bool, string)>(new UnboundedChannelOptions { SingleReader = true });
        _stop = new CancellationTokenSource();
        _worker = Task.Run(() => RunAsync(_stop.Token));
    }

    public void EnqueueRegistration(string pendingId) => _queue.Writer.TryWrite((true, pendingId));

    public void EnqueueAccess(string requestId) => _queue.Writer.TryWrite((false, requestId));

    /// <summary>Stops taking and showing approvals at once, without waiting for the worker (process exit).</summary>
    public void Cancel()
    {
        _queue.Writer.TryComplete();
        try
        {
            _stop?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already stopped.
        }
    }

    public async Task StopAsync()
    {
        _queue.Writer.TryComplete();
        _stop?.Cancel();
        if (_worker is not null)
        {
            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Stopping is the point.
            }
        }
        _stop?.Dispose();
        _stop = null;
        _worker = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task RunAsync(CancellationToken stop)
    {
        await foreach (var (isRegistration, id) in _queue.Reader.ReadAllAsync(stop).ConfigureAwait(false))
        {
            try
            {
                if (isRegistration) await PromptRegistrationAsync(id, stop).ConfigureAwait(false);
                else await PromptAccessAsync(id, stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A dialog that failed to open is a refusal, never an approval.
                _audit.Record(new AgentAuditEvent(AgentAuditKind.RequestRejected, _time.GetUtcNow(), null, null,
                    "approval dialog failed: " + ex.GetType().Name));
                if (isRegistration) _registry.Decide(id, false);
                else _leases.Decide(id, AccessDecision.Deny);
            }
        }
    }

    private async Task PromptRegistrationAsync(string pendingId, CancellationToken stop)
    {
        var prompt = _registry.GetPrompt(pendingId);
        var pending = _registry.FindPending(pendingId);
        if (prompt is null || pending is null) return;

        using var item = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var answer = _prompter.ConfirmRegistrationAsync(prompt, item.Token);
        if (await Task.WhenAny(answer, pending.Settled).ConfigureAwait(false) != answer)
        {
            // Expired or cancelled while on screen: close the dialog, decide nothing.
            item.Cancel();
            await Swallow(answer).ConfigureAwait(false);
            return;
        }

        _registry.Decide(pendingId, await answer.ConfigureAwait(false));
    }

    private async Task PromptAccessAsync(string requestId, CancellationToken stop)
    {
        var prompt = _leases.GetPrompt(requestId);
        var request = _leases.FindRequest(requestId);
        if (prompt is null || request is null) return;

        using var item = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var answer = _prompter.ConfirmAccessAsync(prompt, item.Token);
        if (await Task.WhenAny(answer, request.Settled).ConfigureAwait(false) != answer)
        {
            item.Cancel();
            await Swallow(answer).ConfigureAwait(false);
            return;
        }

        var decision = await answer.ConfigureAwait(false);
        if (!_leases.Decide(requestId, decision) && decision.Approved)
        {
            // A grant the manager refused (more than requested) is a denial, not a silent no-op.
            _leases.Decide(requestId, AccessDecision.Deny);
        }
    }

    private static async Task Swallow(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // The dialog was closed on purpose; its outcome no longer matters.
        }
    }
}
