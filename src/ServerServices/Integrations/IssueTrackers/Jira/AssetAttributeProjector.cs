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

        /// <summary>
        /// Relation target (<see cref="MappableFields.Applications"/>, <see cref="MappableFields.Hosts"/>) → the objects it
        /// names (M58, T291). Absent when the attribute was empty: the register said nothing, so no link is removed.
        /// </summary>
        public Dictionary<string, List<AssetObjectReference>> References { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<AssetObjectReference> ReferencesOf(string field) =>
            References.TryGetValue(field, out var list) ? list : [];

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
            if (MappableFields.IsRelation(mapping.TargetField))
            {
                var references = ReadReferences(payload, mapping);
                if (references.Count > 0) result.References[mapping.TargetField] = references;
                continue;
            }

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

    /// <summary>
    /// The objects a relation mapping names (M58, T291): the attribute's references by id, then by name; a text attribute
    /// gives one bare name per value; the constant, when the attribute is empty, one name. <c>FirstOfList</c> keeps the
    /// first; duplicates go. Labels are clipped, since they come from a third party and end up in messages.
    /// </summary>
    private static List<AssetObjectReference> ReadReferences(AssetObjectPayload payload, JiraObjectAttributeMapping mapping)
    {
        List<AssetObjectReference>? references = null;

        if (mapping.SourceAttributeId is { } id && payload.References.TryGetValue(id, out var byId) && byId.Count > 0)
            references = byId;
        else if (!string.IsNullOrWhiteSpace(mapping.SourceAttributeName)
                 && payload.ReferencesByName.TryGetValue(mapping.SourceAttributeName, out var byName) && byName.Count > 0)
            references = byName;

        if (references == null)
        {
            List<string>? names = null;
            if (mapping.SourceAttributeId is { } textId && payload.Attributes.TryGetValue(textId, out var textById))
                names = textById;
            else if (!string.IsNullOrWhiteSpace(mapping.SourceAttributeName)
                     && payload.AttributesByName.TryGetValue(mapping.SourceAttributeName, out var textByName))
                names = textByName;

            if ((names == null || names.Count == 0) && !string.IsNullOrWhiteSpace(mapping.ConstantValue))
                names = [mapping.ConstantValue];

            references = (names ?? []).Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => new AssetObjectReference(null, null, n.Trim())).ToList();
        }

        var distinct = references
            .Select(r => r with { Label = r.Label.Length > MaxReferenceLabel ? r.Label[..MaxReferenceLabel] : r.Label })
            .DistinctBy(r => r.ObjectId ?? "label:" + r.Label.ToLowerInvariant())
            .ToList();

        return mapping.Transform == JiraAttributeTransform.FirstOfList ? distinct.Take(1).ToList() : distinct;
    }

    /// <summary>The longest referenced-object label kept: the length of a host name and of an entity's name.</summary>
    private const int MaxReferenceLabel = 255;

    /// <summary>
    /// A CMDB's internet-exposure value as yes, no or unrecognised (M58, T291). Unlike <see cref="ApplyTruthy"/>, which
    /// reads everything outside its yes-list as no, an unrecognised value here is null: reading "Unknown" as "not exposed"
    /// would switch off <c>m_net</c> on the register's say-so when the register said nothing.
    /// </summary>
    internal static bool? ParseInternetFacing(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var value = raw.Trim().ToLowerInvariant();

        if (value is "true" or "yes" or "y" or "1" or "on" or "sim" or "s" or "public" or "público" or "publico"
            or "external" or "externo" or "internet" or "internet-facing" or "internet facing" or "exposed" or "exposto"
            or "dmz") return true;

        if (value is "false" or "no" or "n" or "0" or "off" or "não" or "nao" or "internal" or "interno" or "private"
            or "privado" or "intranet" or "not exposed" or "none") return false;

        return null;
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
