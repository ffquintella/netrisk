using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GUIClient.Tests.Tools.RiskIndex;
using GUIClient.Tools.RiskIndex;
using Xunit;

namespace GUIClient.Tests.Views;

/// <summary>
/// S40 §5.6.1, §5.9.1, §12.4 <c>RiskOverviewDeltaColumnsNotSortableTest</c> (T254): Δ is never a sort (S39 §1.3). The
/// Explore tree's Δ7d, band, type and state columns are built unsortable in the code-behind; the object list cancels the
/// grid's own sort and asks the server, and every column it lets the user sort names one of the server's sort keys.
/// Source scans, because this project does not load Avalonia.
/// </summary>
public class RiskOverviewDeltaColumnsNotSortableTest
{
    private static string View(string name) =>
        File.ReadAllText(Path.Combine(RiskIndexTestText.ClientDirectory(), "Views", "RiskOverview", name));

    [Theory]
    [InlineData("StrColDelta")]
    [InlineData("StrColType")]
    public void TestTheTreesUnsortableTextColumnsAreBuiltUnsortable(string header)
    {
        var source = View("RiskNodeTree.axaml.cs");

        var line = Assert.Single(source.Split('\n'), l => l.Contains("vm." + header, StringComparison.Ordinal));
        Assert.Contains("options: Unsorted()", line);
        Assert.Matches(@"Unsorted\(\)\s*=>\s*new\(\)\s*\{\s*CanUserSortColumn\s*=\s*false\s*\}", source);
    }

    [Theory]
    [InlineData("StrColBand")]
    [InlineData("StrColState")]
    public void TestTheTreesTemplateColumnsWithoutAnOrderDoNotSort(string header)
    {
        var source = View("RiskNodeTree.axaml.cs");

        var start = source.IndexOf("vm." + header, StringComparison.Ordinal);
        Assert.True(start >= 0, header);
        var column = source[start..source.IndexOf("})", start, StringComparison.Ordinal)];
        Assert.Contains("CanUserSortColumn = false", column);
    }

    /// <summary>The comparability rule (§5.6.4): every column that sorts goes through the group-preserving comparison.</summary>
    [Fact]
    public void TestEverySortableTreeColumnKeepsTheGroups()
    {
        var source = View("RiskNodeTree.axaml.cs");

        Assert.Equal(4, Regex.Matches(source, @"options: Sorted\(NodeTreeSortColumn\.").Count);
        Assert.Equal(2, Regex.Matches(source, @"NodeTreeRows\.Compare\(a\?\.Entry, b\?\.Entry, column,").Count);
        Assert.Equal(2, Regex.Matches(source, @"NodeTreeRows\.Compare\(a\?\.Entry, b\?\.Entry, NodeTreeSortColumn\.Node,").Count);
    }

    [Fact]
    public void TestTheObjectListCancelsTheGridsSortAndAsksTheServer()
    {
        var code = View("RiskObjectList.axaml.cs");

        Assert.Contains("ObjectsGrid.Sorting += OnSorting", code);
        Assert.Matches(@"e\.Handled\s*=\s*true;\s*\(DataContext as RiskObjectListViewModel\)\?\.SortBy\(e\.Column\.SortMemberPath\)", code);
    }

    [Fact]
    public void TestEveryObjectColumnEitherDoesNotSortOrNamesAServerSort()
    {
        var view = View("RiskObjectList.axaml");
        var columns = Regex.Matches(view, @"<DataGrid(?:Text|Template)Column(?=[\s/>])[^>]*>").Select(m => m.Value).ToList();
        Assert.True(columns.Count >= 10);

        foreach (var column in columns)
        {
            var sortPath = Regex.Match(column, @"SortMemberPath=""(?<path>[^""]+)""");
            if (sortPath.Success)
                Assert.Contains(sortPath.Groups["path"].Value, ObjectListQuery.SortableColumns);
            else
                Assert.Contains(@"CanUserSort=""False""", column);
        }

        Assert.DoesNotMatch(@"(?i)SortMemberPath=""[^""]*(delta|sensitiv)", view);
    }
}
