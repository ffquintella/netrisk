using DAL.Entities;
using Model.RiskIndex;
using ServerServices.RiskIndex.Read;

namespace ServerServices.RiskIndex.Profiles;

/// <summary>
/// The lifecycle of an index profile (S39 §13.1; S40 §6.2–§6.4, §8.4; M53, T250): draft, validation, preview, submission,
/// segregated approval, rejection, suspension, and the reads of what a profile produced (preview, bridge, history), which
/// carry the organization's values and so need global scope and pass the shadow-pilot rule.
/// </summary>
public interface IRiskIndexProfilesService
{
    Task<IReadOnlyList<RiskIndexProfileSummaryDto>> ListAsync(CancellationToken ct = default);

    Task<RiskIndexProfileDto> GetAsync(int id, CancellationToken ct = default);

    Task<RiskIndexProfileDto?> GetActiveAsync(CancellationToken ct = default);

    Task<RiskIndexProfileDiffDto> DiffAsync(int a, int b, CancellationToken ct = default);

    RiskIndexParameterSchemaDto GetSchema();

    IReadOnlyList<RiskIndexPresetDto> GetPresets();

    Task<RiskIndexProfileDto> CreateAsync(RiskIndexProfileCreateRequest request, RiskIndexCaller caller, CancellationToken ct = default);

    /// <param name="expectedRevision">The client's <c>If-Match</c>; a different stored revision is <c>409 stale_draft</c>.</param>
    Task<RiskIndexProfileDto> UpdateAsync(int id, RiskIndexProfileUpdateRequest request, int expectedRevision, RiskIndexCaller caller,
        CancellationToken ct = default);

    Task DeleteAsync(int id, RiskIndexCaller caller, CancellationToken ct = default);

    Task<RiskIndexValidationResultDto> ValidateAsync(int id, CancellationToken ct = default);

    Task<RiskIndexPreviewStatusDto> RequestPreviewAsync(int id, RiskIndexCaller caller, CancellationToken ct = default);

    Task<RiskIndexPreviewDto> GetPreviewAsync(int id, RiskIndexCaller caller, CancellationToken ct = default);

    Task<RiskIndexProfileDto> SubmitAsync(int id, RiskIndexSubmitRequest request, RiskIndexCaller caller, CancellationToken ct = default);

    /// <summary>The segregation checks the approval refuses on — the same evaluation <see cref="ApproveAsync"/> runs.</summary>
    Task<RiskIndexApprovalEligibilityDto> GetApprovalEligibilityAsync(int id, RiskIndexCaller caller, CancellationToken ct = default);

    Task<RiskIndexProfileDto> ApproveAsync(int id, RiskIndexApproveRequest request, RiskIndexCaller caller, CancellationToken ct = default);

    Task<RiskIndexProfileDto> RejectAsync(int id, RiskIndexRejectRequest request, RiskIndexCaller caller, CancellationToken ct = default);

    Task<RiskIndexProfileDto> RetireAsync(int id, RiskIndexRetireRequest request, RiskIndexCaller caller, CancellationToken ct = default);

    /// <summary>Links the committee's minutes — a file the caller uploaded, with no other parent — to a pending profile.</summary>
    Task AttachAsync(int id, RiskIndexAttachRequest request, RiskIndexCaller caller, CancellationToken ct = default);

    Task<IReadOnlyList<Model.DTO.FileListing>> ListAttachmentsAsync(int id, CancellationToken ct = default);

    Task<RiskIndexBridgeDto> GetBridgeAsync(int id, RiskIndexCaller caller, CancellationToken ct = default);

    Task<IReadOnlyList<AuditLog>> GetHistoryAsync(int id, int limit, RiskIndexCaller caller, CancellationToken ct = default);
}
