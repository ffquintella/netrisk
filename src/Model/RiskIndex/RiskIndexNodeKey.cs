using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DAL.Enums;

namespace Model.RiskIndex;

/// <summary>
/// A dashboard node key (S40 §8.1): <c>org</c>, <c>scope</c>, or an intersection of at most one entity term (<c>e:id</c>
/// or <c>u</c>) and at most one term of each of <c>c:</c> (asset class or tab group), <c>s:</c> (source), <c>k:</c>
/// (effective criticality) and <c>a:</c> (environment), separated by <c>;</c>. <see cref="ToString"/> is the canonical form
/// (order <c>e|u; c; s; k; a</c>) every cache key is built from. Shared by the server and the desktop client.
/// </summary>
public sealed record RiskIndexNodeKey
{
    public const string Organization = "org";
    public const string Scope = "scope";
    public const string UnassignedTerm = "u";

    /// <summary>The longest key accepted; a real key is a few dozen characters.</summary>
    public const int MaxLength = 128;

    public bool IsOrganization { get; private init; }
    public bool IsScopeRoot { get; private init; }
    public int? EntityId { get; private init; }
    public bool Unassigned { get; private init; }

    /// <summary>The asset class (<c>c:server</c> …), when the class term names one class.</summary>
    public RiskIndexAssetClass? AssetClass { get; private init; }

    /// <summary>The tab group (<c>c:devices</c> …), when the class term names a group.</summary>
    public RiskIndexTabGroup? TabGroup { get; private init; }

    public string? Source { get; private init; }
    public int? Criticality { get; private init; }
    public string? Environment { get; private init; }

    /// <summary>True when the key has any class, source, criticality or environment term.</summary>
    public bool HasFilters => AssetClass is not null || TabGroup is not null || Source is not null || Criticality is not null ||
                              Environment is not null;

    /// <summary>The root of the organization ("org").</summary>
    public static RiskIndexNodeKey Org { get; } = new() { IsOrganization = true };

    public static RiskIndexNodeKey ForEntity(int entityId) => new() { EntityId = entityId };

    public static readonly IReadOnlyDictionary<string, RiskIndexAssetClass> ClassTerms =
        new Dictionary<string, RiskIndexAssetClass>(StringComparer.Ordinal)
        {
            ["server"] = RiskIndexAssetClass.Server, ["workstation"] = RiskIndexAssetClass.Workstation,
            ["network"] = RiskIndexAssetClass.Network, ["identity"] = RiskIndexAssetClass.Identity,
            ["application"] = RiskIndexAssetClass.Application, ["internet_facing"] = RiskIndexAssetClass.InternetFacing,
            ["cloud"] = RiskIndexAssetClass.Cloud, ["external_domain"] = RiskIndexAssetClass.ExternalDomain,
            ["ot_iot"] = RiskIndexAssetClass.OtIot, ["unknown"] = RiskIndexAssetClass.Unknown
        };

    public static readonly IReadOnlyDictionary<string, RiskIndexTabGroup> TabTerms =
        new Dictionary<string, RiskIndexTabGroup>(StringComparer.Ordinal)
        {
            ["devices"] = RiskIndexTabGroup.Devices, ["internetfacing"] = RiskIndexTabGroup.InternetFacing,
            ["identities"] = RiskIndexTabGroup.Identities, ["applications"] = RiskIndexTabGroup.Applications,
            ["clouds"] = RiskIndexTabGroup.Clouds
        };

    public static string ClassTerm(RiskIndexAssetClass assetClass) => ClassTerms.First(kv => kv.Value == assetClass).Key;

    public static string TabTerm(RiskIndexTabGroup tab) => TabTerms.First(kv => kv.Value == tab).Key;

    /// <summary>Parses a key; null or blank is <c>org</c>. False for anything outside the grammar.</summary>
    public static bool TryParse(string? text, out RiskIndexNodeKey key)
    {
        key = Org;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (text.Length > MaxLength) return false;

        var trimmed = text.Trim();
        if (trimmed == Organization) return true;
        if (trimmed == Scope)
        {
            key = new RiskIndexNodeKey { IsScopeRoot = true };
            return true;
        }

        var result = new RiskIndexNodeKey();
        bool entity = false, cls = false, source = false, crit = false, env = false;
        foreach (var raw in trimmed.Split(';'))
        {
            var term = raw.Trim();
            if (term == UnassignedTerm)
            {
                if (entity) return false;
                entity = true;
                result = result with { Unassigned = true };
                continue;
            }

            var colon = term.IndexOf(':');
            if (colon <= 0 || colon == term.Length - 1) return false;
            var kind = term[..colon];
            var value = term[(colon + 1)..];
            switch (kind)
            {
                case "e":
                    if (entity || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
                        return false;
                    entity = true;
                    result = result with { EntityId = id };
                    break;
                case "c":
                    if (cls) return false;
                    cls = true;
                    if (ClassTerms.TryGetValue(value, out var c)) result = result with { AssetClass = c };
                    else if (TabTerms.TryGetValue(value, out var t)) result = result with { TabGroup = t };
                    else return false;
                    break;
                case "s":
                    if (source || !IsSourceTerm(value)) return false;
                    source = true;
                    result = result with { Source = value };
                    break;
                case "k":
                    if (crit || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var k) || k is < 1 or > 5)
                        return false;
                    crit = true;
                    result = result with { Criticality = k };
                    break;
                case "a":
                    if (env || !IsEnvironmentTerm(value)) return false;
                    env = true;
                    result = result with { Environment = value };
                    break;
                default:
                    return false;
            }
        }

        key = result;
        return true;
    }

    /// <summary>A source id of the S39 §3.2 catalogue: upper-case letters, digits and hyphens.</summary>
    private static bool IsSourceTerm(string value) =>
        value.Length is > 0 and <= 32 && value.All(ch => ch is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-');

    /// <summary>A vocabulary term of <c>ambientes_nao_producao</c>, <c>prod</c> or <c>none</c>: lower-case word characters.</summary>
    private static bool IsEnvironmentTerm(string value) =>
        value.Length is > 0 and <= 32 && value.All(ch => ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-');

    /// <summary>The key intersected with one asset class (replacing any class or tab term).</summary>
    public RiskIndexNodeKey WithAssetClass(RiskIndexAssetClass assetClass) => this with { AssetClass = assetClass, TabGroup = null };

    /// <summary>The key intersected with one tab group (replacing any class or tab term).</summary>
    public RiskIndexNodeKey WithTabGroup(RiskIndexTabGroup tab) => this with { AssetClass = null, TabGroup = tab };

    public RiskIndexNodeKey WithSource(string source) =>
        IsSourceTerm(source) ? this with { Source = source } : throw new ArgumentException("Not a source id.", nameof(source));

    public RiskIndexNodeKey WithCriticality(int criticality) =>
        criticality is >= 1 and <= 5 ? this with { Criticality = criticality }
            : throw new ArgumentOutOfRangeException(nameof(criticality));

    public RiskIndexNodeKey WithEnvironment(string environment) =>
        IsEnvironmentTerm(environment) ? this with { Environment = environment }
            : throw new ArgumentException("Not an environment term.", nameof(environment));

    /// <summary>The key with <paramref name="other"/>'s filter terms added (a child under the current filters).</summary>
    public RiskIndexNodeKey WithFiltersOf(RiskIndexNodeKey other) => this with
    {
        AssetClass = AssetClass ?? other.AssetClass,
        TabGroup = TabGroup ?? (AssetClass is null ? other.TabGroup : null),
        Source = Source ?? other.Source,
        Criticality = Criticality ?? other.Criticality,
        Environment = Environment ?? other.Environment
    };

    /// <summary>The same key without its filter terms: the entity part, or <c>org</c>.</summary>
    public RiskIndexNodeKey EntityPart => IsScopeRoot || IsOrganization ? this
        : EntityId is not null ? ForEntity(EntityId.Value)
        : Unassigned ? new RiskIndexNodeKey { Unassigned = true }
        : Org;

    /// <summary>The canonical form: <c>e|u; c; s; k; a</c>.</summary>
    public override string ToString()
    {
        if (IsScopeRoot) return Scope;
        var terms = new List<string>();
        if (EntityId is { } e) terms.Add("e:" + e.ToString(CultureInfo.InvariantCulture));
        else if (Unassigned) terms.Add(UnassignedTerm);
        if (AssetClass is { } c) terms.Add("c:" + ClassTerm(c));
        else if (TabGroup is { } t) terms.Add("c:" + TabTerm(t));
        if (Source is { } s) terms.Add("s:" + s);
        if (Criticality is { } k) terms.Add("k:" + k.ToString(CultureInfo.InvariantCulture));
        if (Environment is { } a) terms.Add("a:" + a);
        return terms.Count == 0 ? Organization : string.Join(';', terms);
    }

    public bool Equals(RiskIndexNodeKey? other) => other is not null && ToString() == other.ToString();

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(ToString());
}
