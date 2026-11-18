using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json.Nodes;
using Model.RiskIndex;

namespace GUIClient.Tools.RiskIndex;

/// <summary>How a parameter row is edited in the draft editor (S40 §6.2.2).</summary>
public enum ProfileParameterKind
{
    Decimal = 1,
    Integer = 2,
    Boolean = 3,
    Choice = 4,
    TextList = 5,
    IntegerList = 6,
    CidrList = 7
}

/// <summary>
/// One editable row of the draft editor: a key of S39 §12.2 (or one entry of a map such as <c>u.EXP.D</c>), its JSON path in
/// the parameters document, the control's clamp and step, and its lock. The ranges that matter are the profile rules'
/// (<see cref="RiskIndexProfileRules"/>): the clamp only keeps a control inside them, the rules report the rest.
/// </summary>
public sealed record ProfileParameterRowSpec(string Key, string Group, ProfileParameterKind Kind, IReadOnlyList<string> Path,
    double Minimum, double Maximum, double Increment, bool Locked, string? LockedReasonKey, string LabelKey, string HelpKey,
    string? SubLabel, IReadOnlyList<string> Choices);

/// <summary>
/// The draft editor's rows built from the server's schema (<c>GET /RiskIndex/Profiles/Schema</c>) and the draft's own maps,
/// and the edits applied through the parameters' JSON document, so every key round-trips through the one strict serializer
/// the server stores (<see cref="RiskIndexParametersJson"/>). Pure, so it is tested without Avalonia (S40 §12.4).
/// </summary>
public static class ProfileParameterRows
{
    /// <summary>The editor's groups, in the order of S40 §6.2.2.</summary>
    public static readonly IReadOnlyList<string> GroupOrder =
        ["weights", "aggregation", "criticality", "normalization", "time", "governance", "bands", "display"];

    /// <summary>Keys edited outside the generic rows: the bands grid, and the Sources and freshness tab (S40 §6.5).</summary>
    public static readonly IReadOnlySet<string> EditedElsewhere =
        new HashSet<string>(StringComparer.Ordinal) { "faixas", "fontes_habilitadas", "frescor_W" };

    public static string GroupLabelKey(string group) => "RiskParamGroup_" + group;

    public static string LabelKey(string key) => "RiskParam_" + key.Replace('.', '_') + "_Label";

    public static string HelpKey(string key) => "RiskParam_" + key.Replace('.', '_') + "_Help";

    /// <summary>The composite keys whose entries are rows of their own, with each entry's clamp, step and kind.</summary>
    private static readonly IReadOnlyDictionary<string, (ProfileParameterKind Kind, double Min, double Max, double Step)> MapEntries =
        new Dictionary<string, (ProfileParameterKind, double, double, double)>(StringComparer.Ordinal)
        {
            ["u"] = (ProfileParameterKind.Decimal, 0.5, 1.0, 0.05),
            ["cotas_populacao"] = (ProfileParameterKind.Decimal, 0.2, 0.8, 0.05),
            ["ancoras_severidade"] = (ProfileParameterKind.Decimal, 0, 1, 0.05),
            ["ancoras_ssc"] = (ProfileParameterKind.Decimal, 0, 1, 0.05),
            ["ancoras_incidente_categoria"] = (ProfileParameterKind.Decimal, 0, 1, 0.05),
            ["ancoras_incidente_severidade"] = (ProfileParameterKind.Decimal, 0, 1, 0.05),
            ["cadencia_revisao"] = (ProfileParameterKind.Integer, 7, 365, 1),
            ["limites_registro"] = (ProfileParameterKind.Decimal, 0, 10, 0.5)
        };

    /// <summary>Every editable row, grouped in <see cref="GroupOrder"/> and in the schema's order inside a group.</summary>
    public static IReadOnlyList<ProfileParameterRowSpec> Build(RiskIndexParameterSchemaDto schema, RiskIndexParameters draft)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(draft);
        var document = Document(draft);
        var rows = new List<ProfileParameterRowSpec>();

        foreach (var item in schema.Items.Where(i => !EditedElsewhere.Contains(i.Key)))
        {
            switch (item.Type)
            {
                case "double":
                case "int":
                    rows.Add(Scalar(item));
                    break;
                case "map" or "object" when MapEntries.TryGetValue(item.Key, out var entry):
                    rows.AddRange(Entries(item, entry, document));
                    break;
                case "bool":
                    rows.Add(Simple(item, ProfileParameterKind.Boolean, []));
                    break;
                case "enum":
                    rows.Add(Simple(item, ProfileParameterKind.Choice, Enum.GetNames<RiskIndexCadenceBasis>()));
                    break;
                case "list":
                    rows.Add(Simple(item, item.Key switch
                    {
                        "niveis_sensiveis" => ProfileParameterKind.IntegerList,
                        "faixas_publicas_internas" => ProfileParameterKind.CidrList,
                        _ => ProfileParameterKind.TextList
                    }, []));
                    break;
            }
        }

        return rows.OrderBy(r => IndexOf(r.Group)).ToList();
    }

    private static int IndexOf(string group)
    {
        var index = GroupOrder.ToList().IndexOf(group);
        return index < 0 ? int.MaxValue : index;
    }

    private static ProfileParameterRowSpec Scalar(RiskIndexParameterSchemaItemDto item)
    {
        var min = Parse(item.Min);
        var max = Parse(item.Max);
        var integer = item.Type == "int";
        // "0 or [min; max]" (the optional categories): the control reaches 0, the rules refuse what lies between.
        var zeroAllowed = item.Key.StartsWith("omega.", StringComparison.Ordinal) && item.Key != "omega.REG";
        var step = integer ? 1 : max - min > 2 ? 0.1 : 0.01;
        return new ProfileParameterRowSpec(item.Key, item.Group, integer ? ProfileParameterKind.Integer : ProfileParameterKind.Decimal,
            PathOf(item.Key), zeroAllowed ? 0 : min, max, step, item.Locked, item.LockedReasonKey, LabelKey(item.Key),
            HelpKey(item.Key), null, []);
    }

    private static ProfileParameterRowSpec Simple(RiskIndexParameterSchemaItemDto item, ProfileParameterKind kind,
        IReadOnlyList<string> choices) =>
        new(item.Key, item.Group, kind, [item.Key], 0, 0, 0, item.Locked, item.LockedReasonKey, LabelKey(item.Key),
            HelpKey(item.Key), null, choices);

    private static IEnumerable<ProfileParameterRowSpec> Entries(RiskIndexParameterSchemaItemDto item,
        (ProfileParameterKind Kind, double Min, double Max, double Step) entry, JsonObject document)
    {
        if (document[item.Key] is not JsonObject map) yield break;
        foreach (var (sub, value) in map)
        {
            if (value is JsonObject nested)
            {
                // cotas_populacao: category → population → share.
                foreach (var (inner, _) in nested)
                    yield return new ProfileParameterRowSpec($"{item.Key}.{sub}.{inner}", item.Group, entry.Kind,
                        [item.Key, sub, inner], entry.Min, entry.Max, entry.Step, false, null, LabelKey(item.Key),
                        HelpKey(item.Key), $"{sub} · {inner}", []);
                continue;
            }

            yield return new ProfileParameterRowSpec($"{item.Key}.{sub}", item.Group, entry.Kind, [item.Key, sub], entry.Min,
                entry.Max, entry.Step, false, null, LabelKey(item.Key), HelpKey(item.Key), sub, []);
        }
    }

    /// <summary>A scalar's JSON path: <c>omega.REG</c> and <c>s.APT_BRK</c> are nested; every other scalar is top-level.</summary>
    public static IReadOnlyList<string> PathOf(string key)
    {
        var dot = key.IndexOf('.');
        return dot > 0 && key[..dot] is "omega" or "s" ? [key[..dot], key[(dot + 1)..]] : [key];
    }

    // ---------- reading and editing the document ----------

    public static JsonObject Document(RiskIndexParameters parameters) =>
        JsonNode.Parse(RiskIndexParametersJson.Serialize(parameters))!.AsObject();

    public static JsonNode? Read(RiskIndexParameters parameters, IReadOnlyList<string> path) => Find(Document(parameters), path);

    private static JsonNode? Find(JsonObject document, IReadOnlyList<string> path)
    {
        JsonNode? node = document;
        foreach (var segment in path)
        {
            if (node is not JsonObject obj || !obj.TryGetPropertyValue(segment, out node)) return null;
        }

        return node;
    }

    public static double? ReadNumber(RiskIndexParameters parameters, IReadOnlyList<string> path) =>
        Read(parameters, path) is JsonValue value && value.TryGetValue<double>(out var number) ? number : null;

    /// <summary>A list or a choice as the text box shows it: items joined by ", ".</summary>
    public static string ReadText(RiskIndexParameters parameters, IReadOnlyList<string> path) => Read(parameters, path) switch
    {
        JsonArray array => string.Join(", ", array.Select(n => n?.ToString() ?? "")),
        JsonValue value => value.ToString(),
        _ => ""
    };

    public static bool ReadBoolean(RiskIndexParameters parameters, IReadOnlyList<string> path) =>
        Read(parameters, path) is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    /// <summary>Sets a number (an integer row keeps an integer token: the stored document is strict about it).</summary>
    public static RiskIndexParameters WithNumber(RiskIndexParameters parameters, ProfileParameterRowSpec row, double value) =>
        With(parameters, row.Path, row.Kind == ProfileParameterKind.Integer
            ? JsonValue.Create((int)Math.Round(value, MidpointRounding.AwayFromZero))
            : JsonValue.Create(Math.Round(value, 6)));

    public static RiskIndexParameters WithBoolean(RiskIndexParameters parameters, ProfileParameterRowSpec row, bool value) =>
        With(parameters, row.Path, JsonValue.Create(value));

    public static RiskIndexParameters WithChoice(RiskIndexParameters parameters, ProfileParameterRowSpec row, string value) =>
        With(parameters, row.Path, JsonValue.Create(value));

    /// <summary>
    /// Sets a list from its text, or returns null when an item does not parse (a non-integer level, an invalid CIDR): that
    /// is the structural invalidity that blocks Save (S40 §6.2.1), never a value silently dropped.
    /// </summary>
    public static RiskIndexParameters? WithList(RiskIndexParameters parameters, ProfileParameterRowSpec row, string? text)
    {
        var items = SplitList(text);
        var array = new JsonArray();
        foreach (var item in items)
        {
            switch (row.Kind)
            {
                case ProfileParameterKind.IntegerList:
                    if (!int.TryParse(item, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) return null;
                    array.Add(number);
                    break;
                case ProfileParameterKind.CidrList:
                    if (!IPNetwork.TryParse(item, out _)) return null;
                    array.Add(item);
                    break;
                default:
                    array.Add(item);
                    break;
            }
        }

        return With(parameters, row.Path, array);
    }

    public static IReadOnlyList<string> SplitList(string? text) =>
        (text ?? "").Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.Ordinal).ToList();

    private static RiskIndexParameters With(RiskIndexParameters parameters, IReadOnlyList<string> path, JsonNode value)
    {
        var document = Document(parameters);
        JsonObject target = document;
        for (var i = 0; i < path.Count - 1; i++)
            target = target[path[i]] as JsonObject ?? throw new ArgumentException($"No object at {string.Join('.', path.Take(i + 1))}.");
        target[path[^1]] = value;
        return RiskIndexParametersJson.Deserialize(document.ToJsonString());
    }

    // ---------- validation issues ----------

    /// <summary>
    /// The issues shown under a row: the issue names the row itself, or an entry family the row belongs to
    /// (<c>cotas_populacao.GOV</c> under <c>cotas_populacao.GOV.Scenarios</c>). An issue about a whole composite
    /// (<c>omega</c>, <c>ancoras_ssc</c>, <c>faixas</c>) is shown once, in its group's summary.
    /// </summary>
    public static IReadOnlyList<RiskIndexValidationIssueDto> IssuesForRow(string rowKey,
        IEnumerable<RiskIndexValidationIssueDto> issues) =>
        issues.Where(i => i.ParameterKey is { } key &&
                          (key == rowKey || (key.Contains('.') && rowKey.StartsWith(key + ".", StringComparison.Ordinal))))
            .ToList();

    /// <summary>The issues of a group that no row shows: the composite ones and those naming no row of the editor.</summary>
    public static IReadOnlyList<RiskIndexValidationIssueDto> IssuesForGroup(string group,
        IEnumerable<RiskIndexValidationIssueDto> issues, IReadOnlyCollection<string> rowKeys) =>
        issues.Where(i => i.ParameterKey is { } key && RiskIndexProfileRules.GroupOf(key) == group &&
                          !rowKeys.Any(row => IssuesForRow(row, [i]).Count > 0))
            .ToList();

    /// <summary>The groups whose parameters differ between the last saved draft and the edit (the editor's change marks).</summary>
    public static IReadOnlySet<string> ChangedGroups(RiskIndexParameters saved, RiskIndexParameters edited) =>
        RiskIndexProfileRules.ChangedKeys(saved, edited)
            .Select(key => key is "fontes_habilitadas" or "frescor_W" ? "sources" : RiskIndexProfileRules.GroupOf(key))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>A value as the diff and the read-only cells show it (invariant numbers, no JSON quotes).</summary>
    public static string Display(JsonNode? node) => node switch
    {
        null => "—",
        JsonValue value when value.TryGetValue<double>(out var number) => number.ToString("0.######", CultureInfo.CurrentCulture),
        JsonValue value => value.ToString(),
        _ => node.ToJsonString()
    };

    private static double Parse(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
}
