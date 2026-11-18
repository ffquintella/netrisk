using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DAL.Entities;
using Model.DTO;
using Model.RiskIndex;

namespace ClientServices.Interfaces;

/// <summary>
/// REST client for the index's configuration API (S40 §8.4, §8.9; M55): the profile lifecycle — draft, validation, preview,
/// submission, approval, rejection, suspension, evidence, bridge and history — and the publication switch. The reads the
/// dashboard already uses (list, one profile, diff, publication) stay on <see cref="IRiskIndexService"/>. A refusal is an
/// <see cref="Model.Exceptions.InvalidHttpRequestException"/> with the status, the API's <c>error</c> code and its sentence.
/// </summary>
public interface IRiskIndexConfigService
{
    /// <summary><c>GET /RiskIndex/Profiles/Schema</c>: every parameter key with its group, range, default and lock.</summary>
    Task<RiskIndexParameterSchemaDto> GetSchemaAsync(CancellationToken ct = default);

    /// <summary><c>GET /RiskIndex/Profiles/Presets</c> (<c>risk_index_configure</c>).</summary>
    Task<IReadOnlyList<RiskIndexPresetDto>> GetPresetsAsync(CancellationToken ct = default);

    /// <summary><c>GET /RiskIndex/Profiles/Active</c>; null when no profile is active (204).</summary>
    Task<RiskIndexProfileDto?> GetActiveAsync(CancellationToken ct = default);

    /// <summary><c>POST /RiskIndex/Profiles</c>: a new draft from the active profile, a preset or a copy of a version.</summary>
    Task<RiskIndexProfileDto> CreateDraftAsync(RiskIndexProfileCreateRequest request, CancellationToken ct = default);

    /// <summary><c>PUT /RiskIndex/Profiles/{id}</c> with <c>If-Match: ‹revision›</c>; a stale revision is <c>409 stale_draft</c>.</summary>
    Task<RiskIndexProfileDto> UpdateDraftAsync(int id, int revision, RiskIndexProfileUpdateRequest request,
        CancellationToken ct = default);

    /// <summary><c>DELETE /RiskIndex/Profiles/{id}</c>: only a draft.</summary>
    Task DeleteDraftAsync(int id, CancellationToken ct = default);

    /// <summary><c>POST /RiskIndex/Profiles/{id}/Validate</c>: the server's authoritative validation.</summary>
    Task<RiskIndexValidationResultDto> ValidateAsync(int id, CancellationToken ct = default);

    /// <summary><c>POST /RiskIndex/Profiles/{id}/Preview</c> (202): queues the sensitivity preview.</summary>
    Task<RiskIndexPreviewStatusDto> RequestPreviewAsync(int id, CancellationToken ct = default);

    /// <summary><c>GET /RiskIndex/Profiles/{id}/Preview</c>: the state and, once completed, the report.</summary>
    Task<RiskIndexPreviewDto> GetPreviewAsync(int id, CancellationToken ct = default);

    /// <summary><c>POST /RiskIndex/Profiles/{id}/Submit</c>.</summary>
    Task<RiskIndexProfileDto> SubmitAsync(int id, RiskIndexSubmitRequest request, CancellationToken ct = default);

    /// <summary><c>GET /RiskIndex/Profiles/{id}/ApprovalEligibility</c> (<c>risk_index_approve</c>): the server's SoD checks.</summary>
    Task<RiskIndexApprovalEligibilityDto> GetApprovalEligibilityAsync(int id, CancellationToken ct = default);

    /// <summary><c>POST /RiskIndex/Profiles/{id}/Approve</c>.</summary>
    Task<RiskIndexProfileDto> ApproveAsync(int id, RiskIndexApproveRequest request, CancellationToken ct = default);

    /// <summary><c>POST /RiskIndex/Profiles/{id}/Reject</c>.</summary>
    Task<RiskIndexProfileDto> RejectAsync(int id, RiskIndexRejectRequest request, CancellationToken ct = default);

    /// <summary><c>POST /RiskIndex/Profiles/{id}/Retire</c>: suspends publication of the active profile.</summary>
    Task<RiskIndexProfileDto> RetireAsync(int id, RiskIndexRetireRequest request, CancellationToken ct = default);

    /// <summary><c>POST /RiskIndex/Profiles/{id}/Attachments</c>: links the caller's uploaded minutes.</summary>
    Task AttachEvidenceAsync(int id, int fileId, CancellationToken ct = default);

    /// <summary><c>GET /RiskIndex/Profiles/{id}/Attachments</c>.</summary>
    Task<IReadOnlyList<FileListing>> GetEvidenceAsync(int id, CancellationToken ct = default);

    /// <summary><c>GET /RiskIndex/Profiles/{id}/Bridge</c>; null when the profile has no bridge yet (404 without a code).</summary>
    Task<RiskIndexBridgeDto?> GetBridgeAsync(int id, CancellationToken ct = default);

    /// <summary><c>GET /RiskIndex/Profiles/{id}/History</c> (unrestricted scope).</summary>
    Task<IReadOnlyList<AuditLog>> GetHistoryAsync(int id, int limit = 500, CancellationToken ct = default);

    /// <summary><c>PUT /RiskIndex/Publication</c> (<c>risk_index_approve</c>).</summary>
    Task<RiskIndexPublicationDto> SetPublicationAsync(RiskIndexPublicationState state, CancellationToken ct = default);
}
