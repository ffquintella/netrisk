using System;

namespace GUIClient.Tools.Hosts;

/// <summary>
/// The <c>first–last of total</c> footer of a paged list (S38 §3.1), and whether there is a page
/// either side. 1-based; an empty result is <c>0–0 of 0</c> with neither direction available.
/// </summary>
public readonly record struct PageRange(int First, int Last, int Total, bool HasPrevious, bool HasNext)
{
    /// <param name="page">1-based page number; anything below 1 is page 1.</param>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="total">Rows the filter matches across every page.</param>
    /// <param name="rowsOnPage">Rows the server actually returned for this page.</param>
    public static PageRange Of(int page, int pageSize, int total, int rowsOnPage)
    {
        page = Math.Max(1, page);
        pageSize = Math.Max(1, pageSize);
        total = Math.Max(0, total);
        rowsOnPage = Math.Max(0, rowsOnPage);

        if (rowsOnPage == 0)
            return new PageRange(0, 0, total, page > 1, false);

        var first = (page - 1) * pageSize + 1;
        var last = first + rowsOnPage - 1;

        // A total smaller than what was just shown means the header was missing or stale; the rows
        // on screen are the better evidence.
        if (total < last) total = last;

        return new PageRange(first, last, total, page > 1, last < total);
    }
}
