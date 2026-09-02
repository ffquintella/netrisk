using System.Globalization;
using DAL.Entities;
using DAL.Enums;

namespace ServerServices.Integrations.IssueTrackers.Jira;

/// <summary>
/// One Assets object plus one mapping, projected into the fields NetRisk will write
/// (Track 4 milestone 4.6).
///
/// A pure function over the payload and the mapping rows — no database, no HTTP — which is what makes
/// the transform behaviour testable without a Jira or a MariaDB, and why the import service can offer
/// a dry run that is genuinely the same code path as a real import.
/// </summary>
public static class AssetAttributeProjector
{
    /// <summary>What a mapping produced for one object.</summary>
    public class ProjectedObject
    {
        /// <summary>Target field name → value, case-insensitive.</summary>
        public Dictionary<string, string> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? Name => Get(MappableFields.Name);

        public string? Owner => Get(MappableFields.Owner);

        public string? Environment => Get(MappableFields.Environment);

        /// <summary>
        /// Null when the mapping has no active-state row at all — which is different from mapping an
        /// attribute that happens to say "false". A missing mapping must leave the record's status
        /// alone; a false one must retire it.
        /// </summary>
        public bool? Active { get; set; }

        public string? Get(string field) =>
            Fields.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

        public int? GetInt(string field) =>
            int.TryParse(Get(field), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
    }

    public static ProjectedObject Project(AssetObjectPayload payload,
        IReadOnlyList<JiraObjectAttributeMapping> mappings)
    {
        var result = new ProjectedObject();

        foreach (var mapping in mappings.OrderBy(m => m.SortOrder))
        {
            var raw = ReadSource(payload, mapping);

            // The constant is a *fallback*, not an override: an attribute that is present wins, and the
            // constant fills the gap. The other way round would make the constant a way to silently
            // ignore the register.
            if (raw == null && !string.IsNullOrWhiteSpace(mapping.ConstantValue))
                raw = mapping.ConstantValue;

            if (raw == null) continue;

            if (string.Equals(mapping.TargetField, MappableFields.Active, StringComparison.OrdinalIgnoreCase))
            {
                result.Active = ApplyTruthy(raw);
                result.Fields[MappableFields.Active] = result.Active == true ? "true" : "false";
                continue;
            }

            var value = Apply(mapping.Transform, raw);

            if (!string.IsNullOrWhiteSpace(value)) result.Fields[mapping.TargetField] = value;
        }

        // The object's own label is the last resort for the name. An Assets object always has one, and
        // a mapping whose name attribute is empty on a handful of objects should still import them
        // rather than reporting "no name" for rows a human can plainly see the name of.
        if (result.Get(MappableFields.Name) == null && !string.IsNullOrWhiteSpace(payload.Label))
            result.Fields[MappableFields.Name] = payload.Label.Trim();

        return result;
    }

    /// <summary>
    /// Reads the mapped attribute, by id first and by name second.
    ///
    /// The name fallback is what lets a mapping survive a schema that was rebuilt: Assets keeps the
    /// attribute names and issues new ids, so an id-only lookup would leave every mapping silently
    /// reading nothing, which presents as "the import stopped working" with no error anywhere.
    /// </summary>
    private static string? ReadSource(AssetObjectPayload payload, JiraObjectAttributeMapping mapping)
    {
        List<string>? values = null;

        if (mapping.SourceAttributeId is { } id && payload.Attributes.TryGetValue(id, out var byId))
            values = byId;

        if ((values == null || values.Count == 0)
            && !string.IsNullOrWhiteSpace(mapping.SourceAttributeName)
            && payload.AttributesByName.TryGetValue(mapping.SourceAttributeName, out var byName))
            values = byName;

        if (values == null || values.Count == 0) return null;

        // A multi-valued attribute joins unless the mapping asked for the first one. Joining is the
        // safer default: silently dropping the second and third owner of a server loses information
        // that the operator can at least see when it is all there.
        return mapping.Transform == JiraAttributeTransform.FirstOfList
            ? values[0]
            : string.Join(", ", values);
    }

    internal static string? Apply(JiraAttributeTransform transform, string raw)
    {
        var value = raw.Trim();

        return transform switch
        {
            JiraAttributeTransform.None => raw,
            JiraAttributeTransform.Trim => value,
            JiraAttributeTransform.Upper => value.ToUpperInvariant(),
            JiraAttributeTransform.Lower => value.ToLowerInvariant(),
            JiraAttributeTransform.TruthyBoolean => ApplyTruthy(value) ? "true" : "false",
            JiraAttributeTransform.FirstOfList => value,
            JiraAttributeTransform.DateTime => ApplyDate(value),
            JiraAttributeTransform.Integer => ApplyInteger(value),
            _ => value
        };
    }

    /// <summary>
    /// Reads a CMDB's idea of "yes".
    ///
    /// The vocabulary matters: an Assets status attribute holds <c>Active</c>, <c>In Production</c> or
    /// <c>In Service</c> far more often than it holds <c>true</c>, and a strict boolean parse would
    /// read every one of those as inactive and retire the estate on first import.
    /// </summary>
    internal static bool ApplyTruthy(string raw)
    {
        var value = raw.Trim().ToLowerInvariant();

        return value is "true" or "yes" or "y" or "1" or "on" or "active" or "enabled" or "in use"
            or "in service" or "in production" or "live" or "operational" or "ativo" or "sim";
    }

    private static string? ApplyDate(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToString("O", CultureInfo.InvariantCulture)
            : null;

    /// <summary>
    /// An integer, taking the first run of digits.
    ///
    /// A CMDB criticality is written as often as "3 - High" or "Tier 2" as it is as "3", and refusing
    /// those would mean the field imports for nobody. The value is clamped by the caller against the
    /// target's range rather than here, since only the caller knows what the range is.
    /// </summary>
    private static string? ApplyInteger(string value)
    {
        var digits = new string(value.SkipWhile(c => !char.IsAsciiDigit(c))
            .TakeWhile(char.IsAsciiDigit).ToArray());

        return digits.Length == 0 ? null : digits;
    }
}
