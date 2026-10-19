using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DAL.Enums;
using Model.RiskIndex;

namespace ClientServices.Interfaces;

/// <summary>
/// REST client for the Cyber Risk Overview's read API (S40 §8.2, §8.9; M54): <c>RiskIndexController</c>'s reads plus the
/// three profile reads the profile chip needs (§5.1), all under <c>risk_index_view</c>. Profile writes, sources and runs
/// belong to the configuration client (M55). A refusal is an <see cref="Model.Exceptions.InvalidHttpRequestException"/>
/// with the status, the API's stable <c>error</c> code (<c>ErrorCode</c>, shown as <c>RiskApi_‹code›MSG</c>) and the
/// server's sentence; a 401 also discards the token; no answer is a <see cref="Model.Exceptions.RestComunicationException"/>.
/// </summary>
public interface IRiskIndexService
{
    /// <summary><c>GET /RiskIndex/Snapshots/Latest</c>; null when there is no snapshot yet (204). Exempt from the shadow rule.</summary>
    Task<RiskIndexLatestDto?> GetLatestAsync(CancellationToken ct = default);

    /// <summary><c>GET /RiskIndex/Snapshots/Dates</c>: the days that have a snapshot, for the date picker's blackout days.</summary>
    Task<IReadOnlyList<DateOnly>> GetSnapshotDatesAsync(DateOnly? from = null, DateOnly? to = null,
        CancellationToken ct = default);

    /// <summary><c>GET /RiskIndex/Overview</c>; <paramref name="node"/> null is the organization.</summary>
    Task<RiskIndexOverviewDto> GetOverviewAsync(string? node = null, DateOnly? date = null, CancellationToken ct = default);

    /// <summary><c>GET /RiskIndex/Categories</c>.</summary>
    Task<IReadOnlyList<RiskIndexCategoryDto>> GetCategoriesAsync(string? node = null, DateOnly? date = null,
        CancellationToken ct = default);

    /// <summary><c>GET /RiskIndex/Trend</c> over 30, 90 or 365 days.</summary>
    Task<RiskIndexTrendDto> GetTrendAsync(string? node = null, int window = 90,
        RiskIndexCategory series = RiskIndexCategory.Icr, DateOnly? date = null, CancellationToken ct = default);

    /// <summary><c>GET /RiskIndex/Nodes</c>: the children of <paramref name="parent"/> along a dimension.</summary>
    Task<RiskIndexNodeChildrenDto> GetNodesAsync(string? parent = null, RiskIndexDimension dimension = RiskIndexDimension.Unit,
        string? filters = null, DateOnly? date = null, CancellationToken ct = default);

    /// <summary><c>GET /RiskIndex/Objects</c>: one page and the <c>X-Total-Count</c> total.</summary>
    Task<(IReadOnlyList<RiskIndexObjectRowDto> Rows, int Total)> GetObjectsAsync(string? node = null, DateOnly? date = null, string? filters = null,
        string? sorts = null, int page = 1, int pageSize = 50, CancellationToken ct = default);

    /// <summary><c>GET /RiskIndex/Objects/{kind}/{reference}</c>.</summary>
    Task<RiskIndexObjectDetailDto> GetObjectAsync(RiskIndexObjectKind kind, string reference, string? node = null,
        DateOnly? date = null, CancellationToken ct = default);

    /// <summary><c>GET /RiskIndex/Inventory</c> of an asset-class tab.</summary>
    Task<RiskIndexInventoryDto> GetInventoryAsync(string? node = null, RiskIndexTabGroup? tab = null, DateOnly? date = null,
        CancellationToken ct = default);

    /// <summary>
    /// <c>GET /RiskIndex/MonthlySummary</c>: the monthly mean of the daily <c>C_k</c> of the node, or of the node ∩
    /// <paramref name="tab"/>, over 6 or 12 months (T253).
    /// </summary>
    Task<RiskIndexMonthlySummaryDto> GetMonthlySummaryAsync(string? node = null, RiskIndexTabGroup? tab = null, int months = 6,
        DateOnly? date = null, CancellationToken ct = default);

    /// <summary><c>GET /RiskIndex/RiskFactors</c> of the node ∩ an asset-class tab: volume and points per factor (T253).</summary>
    Task<IReadOnlyList<RiskIndexRiskFactorDto>> GetRiskFactorsAsync(string? node = null, RiskIndexTabGroup? tab = null,
        DateOnly? date = null, CancellationToken ct = default);

    /// <summary>
    /// <c>GET /RiskIndex/Dimensions</c>: the classes, sources, criticalities and environments present in the node, over the
    /// caller's visible objects — what the Explore panel's "+ Filtro" offers (T254).
    /// </summary>
    Task<RiskIndexDimensionsDto> GetDimensionsAsync(string? node = null, DateOnly? date = null, CancellationToken ct = default);

    /// <summary><c>GET /RiskIndex/Changes</c>: what moved between two snapshots (default: the previous one).</summary>
    Task<RiskIndexChangesDto> GetChangesAsync(string? node = null, DateOnly? from = null, DateOnly? to = null,
        CancellationToken ct = default);

    /// <summary><c>GET /RiskIndex/Contributions</c>, optionally of one category.</summary>
    Task<RiskIndexContributionsDto> GetContributionsAsync(string? node = null, DateOnly? date = null,
        RiskIndexCategory? category = null, CancellationToken ct = default);

    /// <summary><c>GET /RiskIndex/GovernanceIndicators</c> (shown beside the index, never in it).</summary>
    Task<RiskIndexGovernanceIndicatorsDto> GetGovernanceIndicatorsAsync(string? node = null, DateOnly? date = null,
        CancellationToken ct = default);

    /// <summary>
    /// <c>GET /RiskIndex/Quality</c>: the node's data quality — the always-visible strip and its detail (T256). A null count
    /// is one the server does not measure at this node.
    /// </summary>
    Task<RiskIndexQualityDto> GetQualityAsync(string? node = null, DateOnly? date = null, CancellationToken ct = default);

    /// <summary><c>GET /RiskIndex/Publication</c>: shadow pilot or published. Exempt from the shadow rule.</summary>
    Task<RiskIndexPublicationDto> GetPublicationAsync(CancellationToken ct = default);

    /// <summary><c>GET /RiskIndex/Profiles</c>: every version, for the profile chip's dialog.</summary>
    Task<IReadOnlyList<RiskIndexProfileSummaryDto>> GetProfilesAsync(CancellationToken ct = default);

    /// <summary><c>GET /RiskIndex/Profiles/{id}</c>: one version with its parameters.</summary>
    Task<RiskIndexProfileDto> GetProfileAsync(int id, CancellationToken ct = default);

    /// <summary><c>GET /RiskIndex/Profiles/Diff?a=&amp;b=</c>: every parameter of the two versions, changed or not.</summary>
    Task<RiskIndexProfileDiffDto> GetProfileDiffAsync(int a, int b, CancellationToken ct = default);
}
