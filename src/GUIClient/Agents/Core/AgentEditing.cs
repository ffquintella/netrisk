using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace GUIClient.Agents;

/// <summary>The kinds of item an agent call can name: what presence, the activity panel and the write dialog show.</summary>
public static class AgentItemKinds
{
    public const string Risk = "risk";
    public const string Finding = "finding";
    public const string Incident = "incident";
    public const string ResponsePlan = "response_plan";
    public const string Assessment = "assessment";
    public const string AssessmentRun = "assessment_run";
    public const string Entity = "entity";
    public const string Host = "host";
}

/// <summary>One item, by kind and id.</summary>
public sealed record AgentItemRef(string Kind, int Id)
{
    // The id arguments the tools declare, most specific first: a run inside an assessment is the run.
    private static readonly (string Argument, string Kind)[] IdArguments =
    {
        ("risk_id", AgentItemKinds.Risk), ("finding_id", AgentItemKinds.Finding),
        ("incident_id", AgentItemKinds.Incident), ("plan_id", AgentItemKinds.ResponsePlan),
        ("run_id", AgentItemKinds.AssessmentRun), ("assessment_id", AgentItemKinds.Assessment),
        ("entity_id", AgentItemKinds.Entity), ("host_id", AgentItemKinds.Host)
    };

    public override string ToString() => Kind + " " + Id.ToString(CultureInfo.InvariantCulture);

    /// <summary>The item a call names through its id argument, or null (a search, or no valid id).</summary>
    public static AgentItemRef? FromArguments(JsonObject? arguments)
    {
        if (arguments is null) return null;
        foreach (var (argument, kind) in IdArguments)
        {
            if (arguments[argument] is JsonValue v && v.GetValueKind() == JsonValueKind.Number &&
                v.TryGetValue<double>(out var d) && d == Math.Floor(d) && d is >= 1 and <= int.MaxValue)
                return new AgentItemRef(kind, (int)d);
        }
        return null;
    }
}

/// <summary>One field an edit changes, before and after, as the write dialog shows it. Never logged.</summary>
public sealed record AgentFieldChange(string Field, string? Before, string? After);

/// <summary>The result shape of every edit tool: what changed, by name, and the item's new version. No value is echoed.</summary>
public sealed record AgentEditResult(string Outcome, string ItemType, int ItemId, string? Version,
    IReadOnlyList<string> ChangedFields);

/// <summary>
/// What a creation made (T354): the new item's id and version; <see cref="Withheld"/> when the server filed it where the
/// classification policy withholds it (the agent then gets no id); <see cref="NotWritten"/> names fields a later step of
/// the creation could not save (the item exists without them).
/// </summary>
public sealed record AgentCreatedItem(int Id, string? Version, bool Withheld = false, IReadOnlyList<string>? NotWritten = null);

/// <summary>
/// An edit an edit tool has prepared and not yet made: the item, the exact field changes, and the one call that makes it.
/// The dispatcher shows it for confirmation when the lease asks for that, checks the gate again, and only then applies it.
/// </summary>
public sealed class AgentEditPlan
{
    private readonly Func<CancellationToken, Task<string?>> _apply;

    /// <param name="apply">Makes the change through ClientServices; returns the item's new version when it can read it.</param>
    public AgentEditPlan(AgentItemRef item, string itemLabel, IReadOnlyList<AgentFieldChange> changes, string outcome,
        Func<CancellationToken, Task<string?>> apply, string? currentVersion = null)
    {
        CurrentVersion = currentVersion;
        Item = item;
        ItemLabel = itemLabel;
        Changes = changes;
        Outcome = outcome;
        _apply = apply;
    }

    /// <summary>
    /// T354: a new item of <paramref name="kind"/> (id 0 until created). Every change is a new value; the user approves it
    /// as a whole "new item", and the dispatcher reports the id <paramref name="create"/> returns.
    /// </summary>
    public static AgentEditPlan NewItem(string kind, string itemLabel, IReadOnlyList<AgentFieldChange> changes,
        Func<CancellationToken, Task<AgentCreatedItem>> create, string? sideEffectsKey = null)
    {
        AgentEditPlan? plan = null;
        plan = new AgentEditPlan(new AgentItemRef(kind, 0), itemLabel,
            changes.Select(c => c with { Before = null }).ToList(), "created", async ct =>
            {
                var created = await create(ct).ConfigureAwait(false);
                plan!.Created = created;
                return created.Version;
            }) { CreatesItem = true, SideEffectsKey = sideEffectsKey };
        return plan;
    }

    /// <summary>True for a plan that creates a new item (<see cref="NewItem"/>): <see cref="Item"/> has id 0 until then.</summary>
    public bool CreatesItem { get; private init; }

    /// <summary>
    /// T356 review: false for an append (a finding note, an incident timeline entry) that leaves the item's own record
    /// and version as they were. It is shown and highlighted, but a screen's older copy of the item is not stale after it.
    /// </summary>
    public bool MovesVersion { get; init; } = true;

    /// <summary>
    /// T354 review: a localization key for what NetRisk itself does when the change lands (notifications, automations), shown
    /// in the confirmation dialog; null when nothing beyond the change happens.
    /// </summary>
    public string? SideEffectsKey { get; init; }

    /// <summary>What the creation made, once applied; null before, and for every other plan.</summary>
    public AgentCreatedItem? Created { get; private set; }

    public AgentItemRef Item { get; }

    /// <summary>The item's title as NetRisk holds it, clipped. Item text: shown to the user, never to the agent.</summary>
    public string ItemLabel { get; }

    public IReadOnlyList<AgentFieldChange> Changes { get; }

    /// <summary>The item's version as read: what an "unchanged" answer reports.</summary>
    public string? CurrentVersion { get; }

    /// <summary>"updated" or "created" — what the result reports once applied.</summary>
    public string Outcome { get; }

    public Task<string?> ApplyAsync(CancellationToken cancellationToken) => _apply(cancellationToken);
}

/// <summary>
/// A tool that changes data (T354). It never writes from <see cref="IAgentTool.InvokeAsync"/>: it prepares an
/// <see cref="AgentEditPlan"/>, and the dispatcher alone applies it after the edit lease, the per-write confirmation (when
/// the lease asks for it) and a second gate check. The registry refuses an edit tool that is not one of these.
/// </summary>
public interface IAgentEditTool : IAgentTool
{
    Task<AgentEditPlan> PrepareAsync(AgentToolCall call, JsonObject arguments, CancellationToken cancellationToken);

    /// <summary>
    /// T354: a tool that creates new items. Every one of its writes waits for the user's approval, whatever the lease, and
    /// only such a tool may hand the dispatcher a <see cref="AgentEditPlan.NewItem"/> plan.
    /// </summary>
    bool CreatesItems => false;
}

/// <summary>The bounds on what an agent may write (NRM §5.3.5 a maximum size on every input).</summary>
public static class AgentEditLimits
{
    public const int TitleLength = 300;
    public const int TextLength = 4000;
    public const int NoteLength = 2000;
    public const int VersionLength = 32;

    /// <summary>A finding note or an incident timeline entry: <c>nr_actions.Message</c> is <c>varchar(255)</c> (T354).</summary>
    public const int ActionNoteLength = Model.DTO.IncidentTimelineEntryDto.MaxTextLength;

    /// <summary>T359 review #5: a value of more lines, or more blank lines in a row, could hide text below the fold.</summary>
    public const int MaxLines = 20;
    public const int MaxConsecutiveBlankLines = 3;
}

/// <summary>
/// T359 review #6: how many writes a lease without confirm-each-write may make before every further write of that lease
/// waits for the user's approval. A constant, so no agent request can raise it.
/// </summary>
public static class AgentWriteBudget
{
    public const int WritesWithoutConfirmation = 20;

    /// <summary>The activity panel's result for the notice that the budget was reached.</summary>
    public const string ConfirmationRequired = "confirmation_required";
}

/// <summary>
/// The item's concurrency token as the agent sees it (T354). None of the edited records has a server-side token, so the
/// version is a digest of the fields an agent can read and change plus the record's own update stamp. A tool reads the
/// item fresh, compares, and refuses a stale write; withheld fields are never part of it, so it reveals nothing.
/// </summary>
public static class AgentItemVersion
{
    public const string Prefix = "rev-";

    public static string Of(AgentItemRef item, params object?[] parts)
    {
        var text = new StringBuilder(item.ToString());
        foreach (var part in parts)
        {
            var value = part switch
            {
                null => "␀",
                DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
                DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => part.ToString() ?? ""
            };
            text.Append('|').Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
        }
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
        return Prefix + Convert.ToHexString(digest, 0, 8).ToLowerInvariant();
    }
}

/// <summary>
/// Reads and screens an edit tool's arguments (T354). Every argument must be one the tool declares; a name that reads like
/// a credential, a value that looks like one, control characters or text over its bound are refused with a clean error
/// that never repeats what the agent sent.
/// </summary>
public sealed class AgentEditArgs
{
    public const string VersionArgument = "version";

    private readonly JsonObject _arguments;

    public AgentEditArgs(JsonObject arguments, JsonObject schema)
    {
        _arguments = arguments;
        var declared = schema["properties"] as JsonObject ?? new JsonObject();
        foreach (var (name, _) in arguments)
        {
            if (AgentSecretNames.IsCredentialLike(name))
                throw new AgentToolException(AgentToolErrors.Invalid,
                    "NetRisk never accepts a credential field from an agent. Nothing was written.");
            if (!declared.ContainsKey(name))
                throw new AgentToolException(AgentToolErrors.Invalid,
                    "An argument this tool does not declare was sent. Nothing was written.");
        }
    }

    public bool Has(string name) => _arguments[name] is not null;

    public int Id(string name)
    {
        var value = Integer(name) ?? throw Invalid(name, "is required and must be a positive integer");
        if (value < 1) throw Invalid(name, "must be a positive integer");
        return value;
    }

    public int? OptionalId(string name)
    {
        if (!Has(name)) return null;
        return Id(name);
    }

    /// <summary>The version the agent read: required, the token format only.</summary>
    public string Version()
    {
        var text = String(VersionArgument) ?? throw Invalid(VersionArgument,
            "is required: pass the 'version' the read tool returned for this item");
        if (text.Length > AgentEditLimits.VersionLength || !text.StartsWith(AgentItemVersion.Prefix, StringComparison.Ordinal))
            throw Invalid(VersionArgument, "is not a version NetRisk issued");
        return text;
    }

    /// <summary>
    /// Text to store as the user's content, or null when absent. Trimmed; refused (never cut) over
    /// <paramref name="maxLength"/>, with control characters other than line breaks and tabs, or when it looks like a
    /// credential (<see cref="AgentDataPolicy.LooksLikeSecretValue"/>).
    /// </summary>
    public string? Text(string name, int maxLength, bool required = false, bool allowEmpty = false)
    {
        var raw = String(name);
        if (raw is null)
        {
            if (required) throw Invalid(name, "is required");
            return null;
        }
        var text = raw.Trim();
        if (text.Length == 0 && (required || !allowEmpty)) throw Invalid(name, "must not be empty");
        if (text.Length > maxLength) throw Invalid(name, $"must be at most {maxLength} characters");
        if (text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')) ||
            text.Any(c => CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned))
            throw Invalid(name, "contains control or formatting characters");
        if (TooManyLineBreaks(text))
            throw Invalid(name, $"has too many line breaks (at most {AgentEditLimits.MaxLines} lines and " +
                                $"{AgentEditLimits.MaxConsecutiveBlankLines} blank lines in a row)");
        if (AgentDataPolicy.LooksLikeSecretValue(text))
            throw new AgentToolException(AgentToolErrors.Invalid,
                $"Argument '{name}' looks like a credential, which NetRisk never accepts from an agent. Nothing was written.");
        return text;
    }

    private static bool TooManyLineBreaks(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n', '\r');
        if (lines.Length > AgentEditLimits.MaxLines) return true;
        var blank = 0;
        foreach (var line in lines)
        {
            blank = line.Trim().Length == 0 ? blank + 1 : 0;
            if (blank > AgentEditLimits.MaxConsecutiveBlankLines) return true;
        }
        return false;
    }

    /// <summary>One of <paramref name="allowed"/> (lower-case snake case), or null when absent.</summary>
    public string? Choice(string name, IReadOnlyCollection<string> allowed)
    {
        var value = String(name);
        if (value is null) return null;
        if (!allowed.Contains(value, StringComparer.Ordinal))
            throw Invalid(name, "must be one of: " + string.Join(", ", allowed));
        return value;
    }

    private string? String(string name)
    {
        if (!Has(name)) return null;
        if (_arguments[name] is JsonValue v && v.GetValueKind() == JsonValueKind.String) return v.GetValue<string>();
        throw Invalid(name, "must be a string");
    }

    private int? Integer(string name) =>
        _arguments[name] is JsonValue v && v.GetValueKind() == JsonValueKind.Number
        && v.TryGetValue<double>(out var d) && d == Math.Floor(d) && d is >= int.MinValue and <= int.MaxValue
            ? (int)d
            : null;

    private static AgentToolException Invalid(string name, string problem) =>
        new(AgentToolErrors.Invalid, $"Argument '{name}' {problem}. Nothing was written.");

    /// <summary>
    /// The refusal of a write whose item changed after the plan was made — typically while the change waited for the
    /// user's approval (review M63 #2). Nothing was written; the agent must read again.
    /// </summary>
    public static AgentToolException ChangedSincePrepared(string readTool) =>
        new(AgentToolErrors.Stale,
            "This item changed after your change was prepared (for example while it waited for the user's approval). " +
            $"Nothing was written. Read it again with {readTool} and retry with the new 'version' only if your change " +
            "still applies.");

    /// <summary>The refusal of a write against an item that changed since the agent read it (T354).</summary>
    public static AgentToolException Stale(string readTool) =>
        new(AgentToolErrors.Stale,
            $"This item changed since you read it. Nothing was written. Read it again with {readTool} and retry with the " +
            "new 'version' only if your change still applies.");
}

/// <summary>
/// An edit tool (T354): a name, a description, the permission it needs, its argument schema and one delegate that reads
/// the item fresh through ClientServices, checks the agent's version, validates as the GUI does and returns the plan.
/// </summary>
public sealed class AgentEditTool : IAgentEditTool
{
    private readonly JsonObject _schema;
    private readonly Func<AgentToolCall, AgentEditArgs, CancellationToken, Task<AgentEditPlan>> _prepare;

    public AgentEditTool(string name, string description, AgentToolPermission permission, JsonObject schema,
        Func<AgentToolCall, AgentEditArgs, CancellationToken, Task<AgentEditPlan>> prepare, bool createsItems = false)
    {
        Name = name;
        Description = description;
        Permission = permission;
        _schema = schema;
        _prepare = prepare;
        CreatesItems = createsItems;
    }

    public string Name { get; }
    public string Description { get; }
    public bool RequiresEdit => true;

    /// <inheritdoc />
    public bool CreatesItems { get; }
    public AgentToolPermission Permission { get; }
    public Type ResultType => typeof(AgentEditResult);

    public JsonObject InputSchema() => (JsonObject)_schema.DeepClone();

    public Task<AgentEditPlan> PrepareAsync(AgentToolCall call, JsonObject arguments, CancellationToken cancellationToken) =>
        _prepare(call, new AgentEditArgs(arguments, _schema), cancellationToken);

    /// <summary>Never writes: an edit runs only through the dispatcher's confirmation gate (<see cref="PrepareAsync"/>).</summary>
    public Task<JsonObject> InvokeAsync(AgentToolCall call, JsonObject arguments, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Edit tools run only through the dispatcher's gate.");
}
