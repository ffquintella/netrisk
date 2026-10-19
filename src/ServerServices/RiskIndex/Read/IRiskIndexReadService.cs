using DAL.Enums;
using Model.RiskIndex;

namespace ServerServices.RiskIndex.Read;

/// <summary>The query of <c>GET /RiskIndex/Objects</c>: simple <c>key=value</c> filters, sorts and a page (S40 §8.2).</summary>
public sealed record RiskIndexObjectQuery(string? Filters = null, string? Sorts = null, int Page = 1, int PageSize = 50)
{
    public const int MaxPageSize = 500;
}

/// <summary>
/// The dashboard's read API (S40 §8.1–§8.3, §11.3; M53, T249). Every value endpoint applies, in order: the shadow pilot
/// (<c>risk_index_in_shadow</c> unless explicit <c>risk_index_approve</c> or Admin), the node's scope rule (official only
/// when the claims cover its members; partial otherwise), the derived-computation budget, and the per-module redaction of
/// every object it names. Responses are cached by node, date, profile, expanded scope and module permissions.
/// </summary>
public interface IRiskIndexReadService
{
    Task<RiskIndexLatestDto?> GetLatestAsync(CancellationToken ct = default);

    Task<IReadOnlyList<DateOnly>> GetSnapshotDatesAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default);

    Task<RiskIndexPublicationDto> GetPublicationAsync(CancellationToken ct = default);

    Task<RiskIndexPublicationDto> SetPublicationAsync(RiskIndexPublicationState state, RiskIndexCaller caller,
        CancellationToken ct = default);

    Task<RiskIndexOverviewDto> GetOverviewAsync(string? node, DateOnly? date, RiskIndexCaller caller, CancellationToken ct = default);

    Task<IReadOnlyList<RiskIndexCategoryDto>> GetCategoriesAsync(string? node, DateOnly? date, RiskIndexCaller caller,
        CancellationToken ct = default);

    Task<RiskIndexTrendDto> GetTrendAsync(string? node, int window, RiskIndexCategory series, DateOnly? date,
        RiskIndexCaller caller, CancellationToken ct = default);

    Task<RiskIndexNodeChildrenDto> GetNodesAsync(string? parent, RiskIndexDimension dimension, string? filters, DateOnly? date,
        RiskIndexCaller caller, CancellationToken ct = default);

    Task<(IReadOnlyList<RiskIndexObjectRowDto> Rows, int Total)> GetObjectsAsync(string? node, DateOnly? date,
        RiskIndexObjectQuery query, RiskIndexCaller caller, CancellationToken ct = default);

    Task<RiskIndexObjectDetailDto> GetObjectAsync(RiskIndexObjectKind kind, string reference, string? node, DateOnly? date,
        RiskIndexCaller caller, CancellationToken ct = default);

    Task<RiskIndexInventoryDto> GetInventoryAsync(string? node, RiskIndexTabGroup? tab, DateOnly? date, RiskIndexCaller caller,
        CancellationToken ct = default);

    /// <summary>
    /// The monthly mean of the daily <c>C_k</c> of the node, or of the node ∩ an asset-class tab, over 6 or 12 months (S40
    /// §5.5.1, §8.2; T253). Pre-computed nodes read their stored days (<c>risk_index_snapshots</c>, <c>risk_index_class_days</c>)
    /// without touching the object rows; any other node is recomposed day by day under the derived budget.
    /// </summary>
    Task<RiskIndexMonthlySummaryDto> GetMonthlySummaryAsync(string? node, RiskIndexTabGroup? tab, int months, DateOnly? date,
        RiskIndexCaller caller, CancellationToken ct = default);

    /// <summary>The risk factors of the node ∩ an asset-class tab: volume and Euler points by dominant factor (S40 §5.5.1; T253).</summary>
    Task<IReadOnlyList<RiskIndexRiskFactorDto>> GetRiskFactorsAsync(string? node, RiskIndexTabGroup? tab, DateOnly? date,
        RiskIndexCaller caller, CancellationToken ct = default);

    /// <summary>
    /// The classes, sources, criticalities and environments present in the node (S40 §8.2 <c>GET /RiskIndex/Dimensions</c>;
    /// T254), counted over the caller's visible objects only: what the Explore panel's "+ Filtro" offers.
    /// </summary>
    Task<RiskIndexDimensionsDto> GetDimensionsAsync(string? node, DateOnly? date, RiskIndexCaller caller,
        CancellationToken ct = default);

    Task<RiskIndexChangesDto> GetChangesAsync(string? node, DateOnly? from, DateOnly? to, RiskIndexCaller caller,
        CancellationToken ct = default);

    Task<RiskIndexContributionsDto> GetContributionsAsync(string? node, DateOnly? date, RiskIndexCategory? category,
        RiskIndexCaller caller, CancellationToken ct = default);

    Task<RiskIndexGovernanceIndicatorsDto> GetGovernanceIndicatorsAsync(string? node, DateOnly? date, RiskIndexCaller caller,
        CancellationToken ct = default);

    /// <summary>
    /// The node's data quality (S40 §5.12, §8.2 <c>GET /RiskIndex/Quality</c>; T256): coverage and freshness as the overview,
    /// the Unassigned and default-criticality fractions by weight, stale sources and the counts, over the visible objects.
    /// </summary>
    Task<RiskIndexQualityDto> GetQualityAsync(string? node, DateOnly? date, RiskIndexCaller caller, CancellationToken ct = default);
}
