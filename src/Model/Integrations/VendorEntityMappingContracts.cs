using DAL.Enums;

namespace Model.Integrations;

// T263 (S40 §8.3, §8.6): the contract of /Integrations/EntityMappings. Provider is the IntegrationKind as a number.

/// <summary>A stored vendor → entity rule.</summary>
public record VendorEntityMappingDto(int Id, int Provider, int ConnectionId, VendorMatchKind Kind, string Value, string? Label,
    int EntityId, string EntityName, int Priority, RestampPolicy Policy, bool Enabled);

/// <summary>The body of a create, an update and a preview.</summary>
public record VendorEntityMappingUpsert(int Provider, int ConnectionId, VendorMatchKind Kind, string Value, string? Label,
    int EntityId, int Priority, RestampPolicy Policy, bool Enabled);

/// <summary>A tag or group the connection's last sync saw, with how many of its objects carry it.</summary>
public record VendorMappingCandidateDto(VendorMatchKind Kind, string Value, string? Label, int ObjectsSeen);

/// <summary>
/// What a rule would do on the next sync, over the hosts the connection last reported with tags. Every matched host is in
/// exactly one bucket: another rule wins it (<see cref="Conflicts"/>), it has no entity (<see cref="WouldAssign"/>), the
/// rule moves it (<see cref="WouldReassign"/>), or it keeps its entity (<see cref="AlreadyAssigned"/>).
/// </summary>
public record VendorEntityMappingPreviewDto(int Matched, int WouldAssign, int WouldReassign, int Conflicts, int AlreadyAssigned,
    IReadOnlyList<VendorMappingPreviewSampleDto> Sample);

/// <summary>One host of the preview's sample (at most 20); <see cref="OutcomeKey"/> is one of <see cref="VendorMappingOutcomes"/>.</summary>
public record VendorMappingPreviewSampleDto(string ObjectLabel, string? CurrentEntity, string? NewEntity, string OutcomeKey);

/// <summary>The stable outcome keys of a preview sample; the desktop translates each as <c>EntityMappingOutcome_‹key›</c>.</summary>
public static class VendorMappingOutcomes
{
    public const string Assign = "assign";
    public const string Reassign = "reassign";
    public const string Conflict = "conflict";
    public const string AlreadyAssigned = "already_assigned";
}
