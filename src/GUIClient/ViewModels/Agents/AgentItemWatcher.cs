using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using GUIClient.Agents;
using ReactiveUI;
using Serilog;

namespace GUIClient.ViewModels.Agents;

/// <summary>
/// Agent presence and changes for one screen (T356), composed into a view-model: the notice under the selected item
/// ("an agent is reading/editing this", "an agent changed: subject, notes") and a refresh when an agent's write to one of
/// this screen's items lands. Built on the runtime's <see cref="AgentPresence"/> events, marshalled to the UI thread.
/// </summary>
public sealed class AgentItemWatcher : ViewModelBase
{
    private readonly string _kind;
    private readonly Func<int?> _selected;
    private readonly Func<int, Task> _onChanged;
    private readonly AgentPresence? _presence;
    private AgentItemChange? _lastChange;

    /// <param name="kind">The <see cref="AgentItemKinds"/> this screen shows.</param>
    /// <param name="selected">The id of the item the screen shows now, if any.</param>
    /// <param name="onChanged">Reloads the screen's copy after an agent changed item <c>id</c>.</param>
    public AgentItemWatcher(string kind, Func<int?> selected, Func<int, Task> onChanged)
    {
        _kind = kind;
        _selected = selected;
        _onChanged = onChanged;
        try
        {
            _presence = GetService<AgentCoworkService>().Runtime.Presence;
        }
        catch (Exception ex)
        {
            // No agent runtime (a test host): the screen works, without presence.
            Log.Debug("Agent presence unavailable: {Message}", ex.Message);
        }
        if (_presence is null) return;
        _presence.Changed += OnPresenceChanged;
        _presence.ItemChanged += OnItemChanged;
    }

    private string _notice = "";
    public string Notice
    {
        get => _notice;
        set => this.RaiseAndSetIfChanged(ref _notice, value);
    }

    private bool _hasNotice;
    public bool HasNotice
    {
        get => _hasNotice;
        set => this.RaiseAndSetIfChanged(ref _hasNotice, value);
    }

    /// <summary>Call when the selection changes: the notice follows the selected item.</summary>
    public void Refresh()
    {
        if (_presence is null || _selected() is not { } id)
        {
            HasNotice = false;
            Notice = "";
            return;
        }

        var item = new AgentItemRef(_kind, id);
        var now = DateTimeOffset.UtcNow;
        var change = _lastChange is { } last && last.Item == item && now - last.At < AgentPresence.Window ? last : null;
        if (change is not null)
        {
            Notice = string.Format(Localizer["AgentChangedItemMSG"], change.AgentName, string.Join(", ", change.Fields));
            HasNotice = true;
        }
        else if (_presence.PresenceOf(item, now) is { } presence)
        {
            Notice = string.Format(presence.Editing ? Localizer["AgentEditingItemMSG"] : Localizer["AgentReadingItemMSG"],
                presence.AgentName);
            HasNotice = true;
        }
        else
        {
            HasNotice = false;
            Notice = "";
        }
    }

    private void OnPresenceChanged() => Dispatcher.UIThread.Post(Refresh);

    private void OnItemChanged(AgentItemChange change)
    {
        if (change.Item.Kind != _kind) return;
        Dispatcher.UIThread.Post(async () =>
        {
            // Review M63 #4: the screen's reload is the user's own call — never sent with the agent header, whatever
            // context this handler was raised from.
            using var userContext = ClientServices.Http.AgentCallContext.Suspend();
            _lastChange = change;
            try
            {
                await _onChanged(change.Item.Id);
            }
            catch (Exception ex)
            {
                Log.Warning("Refreshing after an agent's change failed: {Message}", ex.Message);
            }
            Refresh();
        });
    }

    public override void Dispose()
    {
        if (_presence is not null)
        {
            _presence.Changed -= OnPresenceChanged;
            _presence.ItemChanged -= OnItemChanged;
        }
        base.Dispose();
    }
}

/// <summary>
/// T356, review M63 #7: an edit screen's guard against saving over an agent's write (<see cref="AgentSaveGuard"/> with the
/// GUI's message). Opened when the screen read its copy; when an agent's change to that item landed later, the screen must
/// not save — its copy is older — and the user is told which fields to reload. The agent's side is the version check.
/// </summary>
public sealed class AgentEditGuard
{
    private readonly AgentSaveGuard _guard;

    private AgentEditGuard(AgentSaveGuard guard) => _guard = guard;

    /// <summary>A guard for a copy read now.</summary>
    public static AgentEditGuard For(string kind, int id) => ForSince(kind, id, DateTimeOffset.UtcNow);

    /// <summary>A guard for a copy the screen read at <paramref name="since"/> (a list loaded earlier).</summary>
    public static AgentEditGuard ForSince(string kind, int id, DateTimeOffset since) =>
        new(new AgentSaveGuard(Presence(), new AgentItemRef(kind, id), since));

    /// <summary>The agent's change that makes this copy stale, if any.</summary>
    public AgentItemChange? Conflict() => _guard.Conflict();

    /// <summary>The message to show instead of saving, or null when no agent changed the item since the copy was read.</summary>
    public string? ConflictMessage() =>
        _guard.Conflict() is { } change
            ? string.Format(ViewModelBase.Localizer["AgentConflictMSG"], change.AgentName, string.Join(", ", change.Fields))
            : null;

    /// <summary>The screen saved or reloaded: it holds the server's copy again.</summary>
    public void Reset() => _guard.Reset(DateTimeOffset.UtcNow);

    private static AgentPresence? Presence()
    {
        try
        {
            return Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<AgentCoworkService>(Program.ServiceProvider).Runtime.Presence;
        }
        catch (Exception ex)
        {
            Log.Debug("Agent presence unavailable: {Message}", ex.Message);
            return null;
        }
    }
}
