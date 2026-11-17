using System;
using System.Collections.Generic;
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

    /// <summary>T357: one edit, shown with its exact change; true only for an explicit approval.</summary>
    Task<bool> ConfirmWriteAsync(WritePrompt prompt, CancellationToken cancellationToken);
}

/// <summary>
/// What the per-write dialog shows (T357): the agent, the tool, the item and every field's old and new value. The values
/// are item text and agent text — shown to the user as plain text, never logged and never sent back to the agent.
/// </summary>
public sealed record WritePrompt(
    string AgentName,
    string AgentFingerprint,
    string ToolName,
    AgentItemRef Item,
    string ItemLabel,
    IReadOnlyList<AgentFieldChange> Changes,
    int WaitingCount);

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
    private Channel<Item> _queue = Channel.CreateUnbounded<Item>();
    private CancellationTokenSource? _stop;
    private Task? _worker;
    private int _queued;

    private enum ItemKind { Registration, Access, Write }

    private sealed record Item(ItemKind Kind, string Id, PendingWrite? Write = null);

    /// <summary>A write waiting for the user: settled true only by the dialog's approval.</summary>
    private sealed class PendingWrite(WritePrompt prompt, CancellationToken cancellation)
    {
        public WritePrompt Prompt { get; } = prompt;
        public CancellationToken Cancellation { get; } = cancellation;
        public TaskCompletionSource<bool> Decision { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

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
        _queue = Channel.CreateUnbounded<Item>(new UnboundedChannelOptions { SingleReader = true });
        Interlocked.Exchange(ref _queued, 0);
        _stop = new CancellationTokenSource();
        _worker = Task.Run(() => RunAsync(_stop.Token));
    }

    public void EnqueueRegistration(string pendingId) => Enqueue(new Item(ItemKind.Registration, pendingId));

    public void EnqueueAccess(string requestId) => Enqueue(new Item(ItemKind.Access, requestId));

    /// <summary>
    /// T357: puts one edit in front of the user, in the same one-at-a-time queue as the other approvals. Completes true
    /// only on an explicit approval; false on deny, when the queue is stopped, or when <paramref name="cancellationToken"/>
    /// fires (access ended, paused or timed out) — which also closes the dialog if it is on screen.
    /// </summary>
    public Task<bool> ConfirmWriteAsync(WritePrompt prompt, CancellationToken cancellationToken)
    {
        if (!IsRunning || cancellationToken.IsCancellationRequested) return Task.FromResult(false);
        var write = new PendingWrite(prompt, cancellationToken);
        var registration = cancellationToken.Register(() => write.Decision.TrySetResult(false));
        write.Decision.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
        if (!Enqueue(new Item(ItemKind.Write, "", write))) write.Decision.TrySetResult(false);
        return write.Decision.Task;
    }

    private bool Enqueue(Item item)
    {
        if (!_queue.Writer.TryWrite(item)) return false;
        Interlocked.Increment(ref _queued);
        return true;
    }

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
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(stop).ConfigureAwait(false))
            {
                Interlocked.Decrement(ref _queued);
                try
                {
                    switch (item.Kind)
                    {
                        case ItemKind.Registration: await PromptRegistrationAsync(item.Id, stop).ConfigureAwait(false); break;
                        case ItemKind.Access: await PromptAccessAsync(item.Id, stop).ConfigureAwait(false); break;
                        default: await PromptWriteAsync(item.Write!, stop).ConfigureAwait(false); break;
                    }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    item.Write?.Decision.TrySetResult(false);
                    return;
                }
                catch (Exception ex)
                {
                    // A dialog that failed to open is a refusal, never an approval.
                    _audit.Record(new AgentAuditEvent(AgentAuditKind.RequestRejected, _time.GetUtcNow(), null, null,
                        "approval dialog failed: " + ex.GetType().Name));
                    switch (item.Kind)
                    {
                        case ItemKind.Registration: _registry.Decide(item.Id, false); break;
                        case ItemKind.Access: _leases.Decide(item.Id, AccessDecision.Deny); break;
                        default: item.Write!.Decision.TrySetResult(false); break;
                    }
                }
            }
        }
        finally
        {
            // Writes still queued when the queue stops are refusals.
            while (_queue.Reader.TryRead(out var left)) left.Write?.Decision.TrySetResult(false);
        }
    }

    private async Task PromptWriteAsync(PendingWrite write, CancellationToken stop)
    {
        if (write.Decision.Task.IsCompleted) return; // cancelled while it waited in the queue

        using var item = CancellationTokenSource.CreateLinkedTokenSource(stop, write.Cancellation);
        var prompt = write.Prompt with { WaitingCount = Math.Max(0, Volatile.Read(ref _queued)) };
        var answer = _prompter.ConfirmWriteAsync(prompt, item.Token);
        if (await Task.WhenAny(answer, write.Decision.Task).ConfigureAwait(false) != answer)
        {
            // Access ended, paused or timed out while on screen: close the dialog, nothing approved.
            item.Cancel();
            await Swallow(answer).ConfigureAwait(false);
            return;
        }

        var approved = await answer.ConfigureAwait(false);
        write.Decision.TrySetResult(approved && !item.IsCancellationRequested);
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
