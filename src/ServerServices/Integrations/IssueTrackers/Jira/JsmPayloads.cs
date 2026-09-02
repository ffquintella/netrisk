namespace ServerServices.Integrations.IssueTrackers.Jira;

/// <summary>
/// One customer request as the Service Desk API reports it (Track 4 milestone 4.6).
///
/// A transport shape, not a view: it exists so the client can be tested against recorded payloads
/// without the mirror's entity or the client's view getting in the way of the parse.
/// </summary>
public class JsmRequest
{
    public string IssueKey { get; set; } = string.Empty;

    public string? IssueId { get; set; }

    public int? ServiceDeskId { get; set; }

    public string? RequestTypeId { get; set; }

    public string? RequestTypeName { get; set; }

    public string? Summary { get; set; }

    public string? StatusName { get; set; }

    /// <summary>Jira's status category key — <c>new</c>, <c>indeterminate</c>, <c>done</c>.</summary>
    public string? StatusCategory { get; set; }

    public string? ReporterAccountId { get; set; }

    public string? ReporterDisplayName { get; set; }

    public string? OrganizationName { get; set; }

    public string? PriorityName { get; set; }

    public string? AssigneeDisplayName { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public bool IsClosed { get; set; }

    public string? RequestUrl { get; set; }

    /// <summary>SLA cycles carried on the request when it was fetched with <c>expand=sla</c>.</summary>
    public List<JsmSlaCycle> Slas { get; set; } = new();
}

/// <summary>One SLA cycle of one metric (Track 4 milestone 4.6).</summary>
public class JsmSlaCycle
{
    public string? MetricId { get; set; }

    public string MetricName { get; set; } = string.Empty;

    public bool IsOngoing { get; set; }

    public bool Breached { get; set; }

    public bool Paused { get; set; }

    public long? GoalDurationMs { get; set; }

    public long? ElapsedMs { get; set; }

    public long? RemainingMs { get; set; }

    public DateTime? CycleStartAt { get; set; }

    public DateTime? CycleStopAt { get; set; }
}

/// <summary>
/// One Assets object as the AQL search reports it (Track 4 milestone 4.6).
///
/// <see cref="Attributes"/> is keyed by <c>objectTypeAttributeId</c> rather than by name, because the
/// AQL response does not reliably carry the attribute's name — that comes from
/// <c>/objecttype/{id}/attributes</c>, which the importer loads once per type anyway to populate the
/// mapping editor. Keying on the id and resolving names separately is what makes the projection work
/// against the payload Assets actually sends.
/// </summary>
public class AssetObjectPayload
{
    public string ObjectId { get; set; } = string.Empty;

    public string? ObjectKey { get; set; }

    public string? Label { get; set; }

    public int? ObjectTypeId { get; set; }

    public string? ObjectTypeName { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    /// <summary>Attribute id → its values, display form preferred.</summary>
    public Dictionary<int, List<string>> Attributes { get; set; } = new();

    /// <summary>
    /// Attribute name → its values, for the attributes whose name the payload did carry. The
    /// fallback lookup when a schema was rebuilt and the mapped ids no longer exist.
    /// </summary>
    public Dictionary<string, List<string>> AttributesByName { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The object as Assets sent it, stored on the audit row verbatim.</summary>
    public string? RawJson { get; set; }
}

/// <summary>One page of an AQL search.</summary>
public class AssetSearchPage
{
    public List<AssetObjectPayload> Objects { get; set; } = new();

    public bool IsLast { get; set; } = true;

    public int? Total { get; set; }
}
