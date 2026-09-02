using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// "This Assets attribute fills that NetRisk field" (Track 4 milestone 4.6) — the object mapping's
/// per-attribute detail.
///
/// Rows rather than a JSON column on <see cref="JiraObjectMapping"/>, because this is what the
/// configuration screen edits as a grid, and a grid over a blob cannot be validated per row (two rows
/// writing the same target field, or a mapping with no identity attribute) without parsing the blob
/// on every keystroke.
/// </summary>
public class JiraObjectAttributeMapping
{
    public int Id { get; set; }

    public int MappingId { get; set; }

    /// <summary>The Assets attribute id, which is what the object payload keys values by.</summary>
    public int? SourceAttributeId { get; set; }

    /// <summary>
    /// The attribute's name. Kept as well as the id, and used as the fallback lookup: an Assets
    /// schema that is rebuilt keeps its attribute names and loses its ids, and a mapping that
    /// survives that is worth the duplicated column.
    /// </summary>
    public string SourceAttributeName { get; set; } = null!;

    /// <summary>
    /// The NetRisk target — <c>Name</c>, <c>Owner</c>, <c>Environment</c>, <c>Active</c>, plus the
    /// host-only <c>HostName</c>, <c>Fqdn</c>, <c>Ip</c>, <c>MacAddress</c>, <c>Os</c>,
    /// <c>OsVersion</c>, <c>Criticality</c>, <c>Comment</c>, and the application-only
    /// <c>Technology</c>. Validated against the target kind's field list on save.
    /// </summary>
    public string TargetField { get; set; } = null!;

    public JiraAttributeTransform Transform { get; set; } = JiraAttributeTransform.None;

    /// <summary>
    /// This attribute takes part in matching an object to an existing NetRisk record. Used by
    /// <see cref="AssetMatchStrategy.NameOnly"/> and, for hosts, to feed the identity chain with the
    /// MAC or FQDN the customer happens to keep in a differently named attribute.
    /// </summary>
    public bool IsIdentity { get; set; }

    /// <summary>Written when the source attribute is absent or empty.</summary>
    public string? ConstantValue { get; set; }

    /// <summary>Grid order. Cosmetic, but a mapping grid that reshuffles itself on every save is not.</summary>
    public int SortOrder { get; set; }

    public virtual JiraObjectMapping? Mapping { get; set; }
}
