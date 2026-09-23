namespace ServerServices.Filtering;

/// <summary>
/// Paging bounds, carried over verbatim from the SieveOptions the API and the test container both
/// configured. Gridify has no equivalent option bag, so the clamp lives here instead.
/// </summary>
public static class FilterBounds
{
    public const int DefaultPageSize = 100;

    public const int MaxPageSize = 1000;

    /// <summary>The page size to actually use: the default when unset, never above the maximum.</summary>
    public static int ResolvePageSize(int? requested) =>
        requested is null or <= 0 ? DefaultPageSize : Math.Min(requested.Value, MaxPageSize);

    /// <summary>The 1-based page to actually use.</summary>
    public static int ResolvePage(int? requested) =>
        requested is null or <= 0 ? 1 : requested.Value;
}
