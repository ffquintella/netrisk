using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using GUIClient.Agents;
using ReactiveUI;
using Serilog;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Agents;

/// <summary>
/// Agent presence and changes for one screen (T356), composed into a view-model: the notice under the selected item
/// ("an agent is reading/editing this", "an agent changed: subject, notes"), a refresh when an agent's write to one of
/// this screen's items lands, and the highlight of each changed control until the user marks it as seen or edits the
/// item (<see cref="Highlights"/>, mapped by <see cref="AgentHighlightScreen"/>). Built on the runtime's
/// <see cref="AgentPresence"/> events, marshalled to the UI thread.
/// </summary>
public sealed class AgentItemWatcher : ViewModelBase
{
    private readonly string _kind;
    private readonly Func<int?> _selected;
    private readonly Func<int, Task> _onChanged;
    private readonly AgentPresence? _presence;
    private readonly AgentHighlightScreen? _screen;
    private AgentItemChange? _lastChange;

    /// <param name="kind">The <see cref="AgentItemKinds"/> this screen shows.</param>
    /// <param name="selected">The id of the item the screen shows now, if any.</param>
    /// <param name="onChanged">Reloads the screen's copy after an agent changed item <c>id</c>.</param>
    /// <param name="screen">Which control shows which field; the kind's screen (<see cref="AgentHighlightScreens.ForKind"/>) when null.</param>
    public AgentItemWatcher(string kind, Func<int?> selected, Func<int, Task> onChanged, AgentHighlightScreen? screen = null)
    {
        _kind = kind;
        _selected = selected;
        _onChanged = onChanged;
        _screen = screen ?? AgentHighlightScreens.ForKind(kind);
        BtAcknowledgeClicked = ReactiveCommand.Create(Acknowledge);
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

    public string StrAcknowledge { get; } = Localizer["AgentAcknowledgeChanges"];

    private AgentFieldHighlights _highlights = AgentFieldHighlights.None;
    /// <summary>T356: the controls of the selected item that show an agent's change not yet seen (bound with a converter).</summary>
    public AgentFieldHighlights Highlights
    {
        get => _highlights;
        set => this.RaiseAndSetIfChanged(ref _highlights, value);
    }

    private bool _hasHighlights;
    /// <summary>Whether the selected item has highlighted changes: shows the "mark as seen" button.</summary>
    public bool HasHighlights
    {
        get => _hasHighlights;
        set => this.RaiseAndSetIfChanged(ref _hasHighlights, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> BtAcknowledgeClicked { get; }

    /// <summary>T356: the user marked the selected item's agent changes as seen; their highlight ends on every screen.</summary>
    public void Acknowledge()
    {
        if (_presence is null || _selected() is not { } id) return;
        _presence.Acknowledge(new AgentItemRef(_kind, id));
        Refresh();
    }

    /// <summary>T356: the user edited <paramref name="fields"/> of the selected item on this screen; their highlight ends.</summary>
    public void UserEdited(params string[] fields)
    {
        if (_presence is null || _selected() is not { } id || fields.Length == 0) return;
        _presence.UserEdited(new AgentItemRef(_kind, id), fields);
    }

    /// <summary>Call when the selection changes: the notice and the highlights follow the selected item.</summary>
    public void Refresh()
    {
        if (_presence is null || _selected() is not { } id)
        {
            HasNotice = false;
            Notice = "";
            Highlights = AgentFieldHighlights.None;
            HasHighlights = false;
            return;
        }

        var item = new AgentItemRef(_kind, id);
        var now = DateTimeOffset.UtcNow;
        var pending = _presence.Unacknowledged(item);
        Highlights = _screen?.StateFor(pending, pending is null ? null
            : string.Format(Localizer["AgentChangedFieldTip"], pending.AgentName)) ?? AgentFieldHighlights.None;
        HasHighlights = Highlights.Any;
        var change = _lastChange is { } last && last.Item == item && now - last.At < AgentPresence.Window ? last : null;
        if (pending is not null)
        {
            // Until the user marks them as seen or edits the item: every field, highlighted or not shown here, is named.
            Notice = string.Format(Localizer["AgentHighlightedItemMSG"], pending.AgentName, string.Join(", ", pending.Fields));
            HasNotice = true;
        }
        else if (change is not null)
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
    private readonly AgentPresence? _presence;

    private AgentEditGuard(AgentSaveGuard guard, AgentPresence? presence)
    {
        _guard = guard;
        _presence = presence;
    }

    /// <summary>T356: the agent's changes to this item not yet seen, for a form that highlights them (or null).</summary>
    public AgentItemChange? Unacknowledged() => _presence?.Unacknowledged(_guard.Item);

    /// <summary>T356: the user edited <paramref name="fields"/> of this item in the form; their highlight ends.</summary>
    public void UserEdited(params string[] fields)
    {
        if (fields.Length > 0) _presence?.UserEdited(_guard.Item, fields);
    }

    /// <summary>A guard for a copy read now.</summary>
    public static AgentEditGuard For(string kind, int id) => ForSince(kind, id, DateTimeOffset.UtcNow);

    /// <summary>A guard for a copy the screen read at <paramref name="since"/> (a list loaded earlier).</summary>
    public static AgentEditGuard ForSince(string kind, int id, DateTimeOffset since)
    {
        var presence = Presence();
        return new AgentEditGuard(new AgentSaveGuard(presence, new AgentItemRef(kind, id), since), presence);
    }

    /// <summary>The agent's change that makes this copy stale, if any.</summary>
    public AgentItemChange? Conflict() => _guard.Conflict();

    /// <summary>The message to show instead of saving, or null when no agent changed the item since the copy was read.</summary>
    public string? ConflictMessage() =>
        _guard.Conflict() is { } change
            ? string.Format(ViewModelBase.Localizer["AgentConflictMSG"], change.AgentName, string.Join(", ", change.Fields))
            : null;

    /// <summary>The screen saved or reloaded: it holds the server's copy again.</summary>
    public void Reset() => _guard.Reset(DateTimeOffset.UtcNow);

    /// <summary>
    /// T356: the user saved this item from the screen — they edited it, so the agent's changes on it are no longer
    /// highlighted anywhere — and the screen holds the server's copy again. Call it only after a save that throws on
    /// failure; for one that can fail silently use <see cref="SaveAsync(AgentEditGuard?, Func{Task{bool}})"/>.
    /// </summary>
    public void Saved()
    {
        Reset();
        _presence?.UserEdited(_guard.Item);
    }

    /// <summary>
    /// T356 review: runs <paramref name="save"/> and, only when it reports success, ends the item's highlights and makes
    /// the copy current (<see cref="AgentSaveGuard.SaveAsync"/>). Without a guard (a new item) it just saves.
    /// </summary>
    public static Task<bool> SaveAsync(AgentEditGuard? guard, Func<Task<bool>> save) =>
        guard is null ? save() : guard._guard.SaveAsync(save, () => DateTimeOffset.UtcNow);

    /// <summary>The same for a synchronous save.</summary>
    public static bool Save(AgentEditGuard? guard, Func<bool> save) =>
        guard is null ? save() : guard._guard.Save(save, () => DateTimeOffset.UtcNow);

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
