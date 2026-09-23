using Gridify;

using ServerServices.Filtering;

namespace ServerServices.Filtering;

/// <summary>
/// Applies a <see cref="ListQuery"/> to a queryable: filter, count, sort, page. Replaces the
/// two-call <c>SieveProcessor.Apply</c> idiom, which ran the filter twice — once with paging
/// suppressed to get the total, once for the rows.
/// </summary>
public static class EntityFilterExtensions
{
    /// <summary>
    /// Filter only — no sort, no paging. The export endpoint streams every matching row.
    /// </summary>
    public static IQueryable<T> ApplyListFilter<T>(
        this IQueryable<T> source, ListQuery query, IGridifyMapper<T> mapper) =>
        source.ApplyFiltering(SieveSyntaxTranslator.TranslateFilter(query.Filters), mapper);

    /// <summary>
    /// The requested page and the total number of rows matching the filter before paging.
    /// </summary>
    public static (List<T> Rows, int TotalCount) ApplyListQuery<T>(
        this IQueryable<T> source, ListQuery query, IGridifyMapper<T> mapper)
    {
        var filtered = source.ApplyFiltering(SieveSyntaxTranslator.TranslateFilter(query.Filters), mapper);

        // Counted before paging: the client renders "n of total", not "n of this page".
        var totalCount = filtered.Count();

        var sort = SieveSyntaxTranslator.TranslateSort(query.Sorts);
        if (!string.IsNullOrWhiteSpace(sort)) filtered = filtered.ApplyOrdering(sort, mapper);

        var pageSize = FilterBounds.ResolvePageSize(query.PageSize);
        var page = FilterBounds.ResolvePage(query.Page);

        return (filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList(), totalCount);
    }
}
