using ServerServices.Filtering;

namespace ServerServices.Filtering;

/// <summary>
/// The paging/filtering/sorting parameters a list endpoint accepts. Property names match the
/// query-string keys Sieve's own model bound, so the HTTP contract is unchanged by the move to
/// Gridify: <c>?filters=…&amp;sorts=…&amp;page=…&amp;pageSize=…</c>.
/// </summary>
public class ListQuery
{
    /// <summary>Filter expression in Sieve syntax; see <see cref="SieveSyntaxTranslator"/>.</summary>
    public string? Filters { get; set; }

    /// <summary>Sort expression in Sieve syntax, <c>-</c> prefix for descending.</summary>
    public string? Sorts { get; set; }

    /// <summary>1-based page number.</summary>
    public int? Page { get; set; }

    public int? PageSize { get; set; }
}
