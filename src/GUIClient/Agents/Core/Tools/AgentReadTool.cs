using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace GUIClient.Agents.ReadTools;

/// <summary>
/// A read tool (T351): a name, a description, the permission it needs, its argument schema and one delegate that calls
/// ClientServices and maps the answer to <typeparamref name="TResult"/>, an allowlist DTO. The result is serialized
/// through that declared type, so nothing the DTO does not name can reach the agent (T352).
/// </summary>
public sealed class AgentReadTool<TResult> : IAgentTool where TResult : class
{
    private readonly JsonObject _schema;
    private readonly Func<AgentToolCall, AgentArgs, CancellationToken, Task<TResult>> _read;

    public AgentReadTool(string name, string description, AgentToolPermission permission, JsonObject schema,
        Func<AgentToolCall, AgentArgs, CancellationToken, Task<TResult>> read)
    {
        Name = name;
        Description = description;
        Permission = permission;
        _schema = schema;
        _read = read;
    }

    public string Name { get; }
    public string Description { get; }
    public bool RequiresEdit => false;
    public AgentToolPermission Permission { get; }
    public Type ResultType => typeof(TResult);

    public JsonObject InputSchema() => (JsonObject)_schema.DeepClone();

    public async Task<JsonObject> InvokeAsync(AgentToolCall call, JsonObject arguments, CancellationToken cancellationToken)
    {
        var result = await _read(call, new AgentArgs(arguments), cancellationToken).ConfigureAwait(false);
        return AgentJson.ToObject(result ?? throw new AgentToolException(AgentToolErrors.NotFound,
            "NetRisk returned nothing for this request."), typeof(TResult));
    }
}

/// <summary>Reads and bounds a tool's arguments; anything out of range is an <c>invalid</c> error naming the argument.</summary>
public sealed class AgentArgs
{
    private readonly JsonObject _arguments;

    public AgentArgs(JsonObject arguments) => _arguments = arguments;

    public int Id(string name)
    {
        if (!Present(name)) throw Invalid(name, "is required");
        var value = Integer(name) ?? throw Invalid(name, "must be a positive integer");
        if (value < 1) throw Invalid(name, "must be a positive integer");
        return value;
    }

    public int? OptionalId(string name)
    {
        if (!Present(name)) return null;
        var value = Integer(name) ?? throw Invalid(name, "must be an integer");
        if (value < 1) throw Invalid(name, "must be a positive integer");
        return value;
    }

    public int? OptionalInt(string name, int min, int max)
    {
        if (!Present(name)) return null;
        var value = Integer(name) ?? throw Invalid(name, "must be an integer");
        if (value < min || value > max) throw Invalid(name, $"must be between {min} and {max}");
        return value;
    }

    public bool Flag(string name, bool fallback)
    {
        if (!Present(name)) return fallback;
        return _arguments[name] is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False
            ? v.GetValue<bool>()
            : throw Invalid(name, "must be true or false");
    }

    /// <summary>Optional text, trimmed, refused (not cut) when longer than <paramref name="maxLength"/>.</summary>
    public string? Text(string name, int maxLength)
    {
        if (!Present(name)) return null;
        if (_arguments[name] is not JsonValue v || v.GetValueKind() != JsonValueKind.String)
            throw Invalid(name, "must be a string");
        var text = v.GetValue<string>().Trim();
        if (text.Length > maxLength) throw Invalid(name, $"must be at most {maxLength} characters");
        return text.Length == 0 ? null : text;
    }

    public (int Page, int PageSize) Paging() =>
        (OptionalInt("page", 1, AgentResultLimits.MaxPage) ?? 1,
         OptionalInt("page_size", 1, AgentResultLimits.MaxPageSize) ?? AgentResultLimits.DefaultPageSize);

    private bool Present(string name) => _arguments[name] is not null;

    private int? Integer(string name) =>
        _arguments[name] is JsonValue v && v.GetValueKind() == JsonValueKind.Number
        && v.TryGetValue<double>(out var d) && d == Math.Floor(d) && d is >= int.MinValue and <= int.MaxValue
            ? (int)d
            : null;

    private static AgentToolException Invalid(string name, string problem) =>
        new(AgentToolErrors.Invalid, $"Argument '{name}' {problem}.");
}

/// <summary>JSON Schema fragments for tool arguments.</summary>
public static class AgentSchema
{
    public static JsonObject Object(params (string Name, JsonObject Schema, bool Required)[] properties)
    {
        var props = new JsonObject();
        foreach (var (name, schema, _) in properties) props[name] = schema.DeepClone();
        var schemaObject = new JsonObject { ["type"] = "object", ["properties"] = props };
        var required = properties.Where(p => p.Required).Select(p => (JsonNode?)JsonValue.Create(p.Name)).ToArray();
        if (required.Length > 0) schemaObject["required"] = new JsonArray(required);
        return schemaObject;
    }

    public static (string, JsonObject, bool) Id(string name, string description) =>
        (name, new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["description"] = description }, true);

    public static (string, JsonObject, bool) OptionalId(string name, string description) =>
        (name, new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["description"] = description }, false);

    public static (string, JsonObject, bool) Text(string name, int maxLength, string description) =>
        (name, new JsonObject { ["type"] = "string", ["maxLength"] = maxLength, ["description"] = description }, false);

    public static (string, JsonObject, bool) Flag(string name, string description) =>
        (name, new JsonObject { ["type"] = "boolean", ["description"] = description }, false);

    public static (string, JsonObject, bool) Int(string name, int min, int max, string description) =>
        (name, new JsonObject { ["type"] = "integer", ["minimum"] = min, ["maximum"] = max, ["description"] = description },
            false);

    public static (string, JsonObject, bool)[] Paging() => new[]
    {
        Int("page", 1, AgentResultLimits.MaxPage, "Page number, from 1."),
        Int("page_size", 1, AgentResultLimits.MaxPageSize,
            $"Items per page (default {AgentResultLimits.DefaultPageSize}, at most {AgentResultLimits.MaxPageSize}).")
    };
}

/// <summary>One page of a search.</summary>
public sealed record AgentPage<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize, bool HasMore)
{
    /// <summary>Pages a list the server returned whole.</summary>
    public static AgentPage<T> Of<TSource>(IEnumerable<TSource> source, int page, int pageSize, Func<TSource, T> map)
    {
        var all = source as IReadOnlyCollection<TSource> ?? source.ToList();
        var items = all.Skip((page - 1) * pageSize).Take(pageSize).Select(map).ToList();
        return new AgentPage<T>(items, all.Count, page, pageSize, page * pageSize < all.Count);
    }
}

/// <summary>A list inside an item, capped at <see cref="AgentResultLimits.MaxNestedItems"/>.</summary>
public sealed record AgentCappedList<T>(IReadOnlyList<T> Items, int Total, bool Truncated)
{
    public static AgentCappedList<T> Of<TSource>(IEnumerable<TSource>? source, Func<TSource, T> map)
    {
        var all = source?.ToList() ?? new List<TSource>();
        var items = all.Take(AgentResultLimits.MaxNestedItems).Select(map).ToList();
        return new AgentCappedList<T>(items, all.Count, all.Count > items.Count);
    }
}
