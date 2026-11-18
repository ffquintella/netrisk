namespace Model.Entities;

// The entity risk-context contract (S40 §6.7, §8.5; M55, T264). Positional records; collections never null.

/// <summary>
/// One entity as the Risk context tab lists it (<c>GET /Entities/RiskContext</c>). <c>Supports‹Field›</c> says whether its
/// definition has the property (S39 §5.2/§5.3); an unsupported field is always null. <see cref="SecurityClassificationId"/>
/// is the id of the <c>securityClassificationLevel</c> entity. <c>LastChanged*</c> is the newest audit row of the three.
/// </summary>
public record EntityRiskContextRowDto(int EntityId, string Name, string DefinitionName, int? ParentId,
    bool SupportsCriticality, int? Criticality, bool SupportsInternetFacing, bool? InternetFacing,
    bool SupportsSecurityClassification, int? SecurityClassificationId, DateTime? LastChangedAtUtc, string? LastChangedBy);

/// <summary>
/// The full desired risk context of one entity in a batch. Null means "not declared" (the property is cleared). A value
/// for a field the entity's definition does not support is refused (<c>field_not_supported_for_type</c>).
/// </summary>
public record EntityRiskContextChange(int EntityId, int? Criticality, bool? InternetFacing, int? SecurityClassificationId);

/// <summary><c>PUT /Entities/RiskContext</c>: one justification for every change, applied in one <c>SaveChanges</c>.</summary>
public record EntityRiskContextBatchRequest(string Justification, IReadOnlyList<EntityRiskContextChange> Changes);

/// <summary>The applied batch: how many entities changed and the batch id shared by its notes and its audit rows.</summary>
public record EntityRiskContextBatchResultDto(int UpdatedEntities, string BatchId);

/// <summary>
/// One change in an entity's history (<c>GET /Entities/{id}/History</c>), never the <c>AuditLog</c> entity itself.
/// <see cref="Justification"/> is the batch's note when the change came from a risk-context batch.
/// </summary>
public record EntityHistoryEntryDto(DateTime OccurredAtUtc, string Action, string Property, string? OldValue,
    string? NewValue, string Actor, string? CorrelationId, string? Justification);

/// <summary>The rules of the risk-context batch both the API and the desktop apply (S40 §6.7).</summary>
public static class EntityRiskContextRules
{
    public const int MinimumJustificationLength = 20;
    public const int MaximumJustificationLength = 2000;
    public const int MaximumBatchSize = 500;
    public const int MinimumCriticality = 1;
    public const int MaximumCriticality = 5;

    /// <summary>The entity types the Risk context tab lists by default.</summary>
    public static readonly IReadOnlyList<string> DefaultTypes =
    [
        "organization", "organizationUnit", "subOrganizationUnit", "businessProcess", "activity", "application",
        "organizationData", "organizationDataGroup"
    ];
}

/// <summary>The stable <c>error</c> codes of the risk-context batch and the history (S40 §8.5).</summary>
public static class EntityRiskContextErrors
{
    public const string FieldNotSupportedForType = "field_not_supported_for_type";
    public const string CriticalityOutOfRange = "criticality_out_of_range";
    public const string ClassificationNotFound = "classification_not_found";
    public const string JustificationTooShort = "justification_too_short";
    public const string JustificationTooLong = "justification_too_long";
    public const string EmptyBatch = "empty_batch";
    public const string BatchTooLarge = "batch_too_large";
    public const string DuplicateEntity = "duplicate_entity_in_batch";
    public const string EntityNotFound = "entity_not_found";
    public const string InvalidLimit = "limit";

    /// <summary>403 on the history: the caller holds neither <c>entity_risk_context</c> nor <c>entities_manage</c>.</summary>
    public const string HistoryRequiresPermission = "entity_history_requires_permission";
}
