using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace GUIClient.Tests.Views;

/// <summary>
/// Keeps a blocking per-cell REST call out of a TreeDataGrid column.
///
/// <c>TeamIdToTeamNameConverter</c> and <c>HostIdToNameConverter</c> turn an id into a name by
/// calling the server, synchronously, because an <c>IValueConverter</c> has no way to be
/// asynchronous. That is tolerable for the one-off detail field they were written for. Used as the
/// value selector of a grid column it is not: the call runs once per cell on the UI thread, and
/// again on every re-render.
///
/// The findings grid did exactly that for its Fix team and Host columns. A page of twenty-odd rows
/// issued about ninety serialised requests, froze the window for several seconds, and — because
/// neither converter caches a failure — logged one error per cell for as long as the server stayed
/// unreachable:
///
/// <code>
/// [14:12:23 ERR] Error getting host
/// [14:12:23 ERR] Error getting team: 1
/// </code>
///
/// The fix is to resolve the ids for the page once, off the UI thread, and let the columns read a
/// map (<c>GUIClient.Tools.RowLabelCache</c>). This test fails on the arrangement that came before
/// it, in the findings grid or in any code-behind-built grid added later.
///
/// Like the other tests in this folder it scans source text: GUIClient.Tests deliberately does not
/// reference GUIClient, because that would drag all of Avalonia into a headless run.
/// </summary>
public class GridColumnBlockingLookupTests
{
    /// <summary>
    /// Converters that reach the server to resolve one row's id.
    ///
    /// Deliberately not "every converter": most are pure formatting, and one
    /// (<c>StringIdToImpactConverter</c>) reads a whole list the service memoises after its first
    /// call, so it costs no per-row request. These four take an id and go and ask about it.
    /// </summary>
    private static readonly string[] PerRowRemoteConverters =
    [
        "TeamIdToTeamNameConverter",
        "HostIdToNameConverter",
        "AnalystIdToAnalystNameConverter",
        "EntityIdToNameConverter"
    ];

    [Fact]
    public void NoGridColumnResolvesItsCellsThroughABlockingConverter()
    {
        var offenders = new List<string>();

        foreach (var file in CodeBehindFiles())
        {
            var source = File.ReadAllText(file);

            if (!source.Contains("Columns.Add(", StringComparison.Ordinal)) continue;

            foreach (var converter in PerRowRemoteConverters)
            {
                // The two ways a code-behind reaches one: by resource key, or by constructing it.
                // Matched as code rather than as bare text so that naming one in a comment — as the
                // fixed view does, to say why it no longer uses it — is not an offence.
                if (source.Contains($"\"{converter}\"", StringComparison.Ordinal)
                    || source.Contains($"new {converter}", StringComparison.Ordinal))
                    offenders.Add($"{Path.GetFileName(file)}: {converter}");
            }
        }

        Assert.True(offenders.Count == 0,
            "A grid column resolves its cells through a converter that makes a blocking REST call. "
            + "That is one synchronous request per cell on the UI thread, repeated on every "
            + "re-render, and one logged error per cell whenever the server is unreachable. Prefetch "
            + "the page's ids in the view model and read them out of a RowLabelCache instead.\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheFindingsGridReadsItsForeignKeyColumnsFromTheLabelMaps()
    {
        var source = File.ReadAllText(Path.Combine(
            GuiClientSourceRoot(), "Views/VulnerabilitiesView.axaml.cs"));

        Assert.Contains("FixTeamLabel(x.FixTeamId)", source, StringComparison.Ordinal);
        Assert.Contains("HostLabel(x.HostId)", source, StringComparison.Ordinal);
        Assert.Contains("AnalystLabel(x.AnalystId)", source, StringComparison.Ordinal);
        Assert.Contains("ApplicationLabel(x.EntityId)", source, StringComparison.Ordinal);

        // The maps are filled after the page lands, so the view has to re-render when they do —
        // otherwise the columns keep showing the bare ids the fallback produced.
        Assert.Contains("RowLabelsVersion", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFindingsViewModelFallsBackOnlyForIdsTheListingsDidNotCover()
    {
        var source = File.ReadAllText(Path.Combine(
            GuiClientSourceRoot(), "ViewModels/VulnerabilitiesViewModel.cs"));

        // The bulk listings are not complete by construction: /Users/Listings holds only enabled
        // accounts, and the "application" definition is not every entity. Naming the row is the
        // point of both columns, so what the listing left over still gets resolved — but only that,
        // and only once, and not on the UI thread. `Missing` over the ids just requested is what
        // distinguishes that from going back to one call per row.
        Assert.Contains("_analystLabels.StillMissing(analystIds)", source, StringComparison.Ordinal);
        Assert.Contains("_applicationLabels.StillMissing(applicationIds)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFindingsViewModelResolvesAPageInBoundedRequests()
    {
        var source = File.ReadAllText(Path.Combine(
            GuiClientSourceRoot(), "ViewModels/VulnerabilitiesViewModel.cs"));

        // Teams, analysts and applications: whole listings, each memoised by its service — one
        // request apiece, not one per row.
        Assert.Contains("TeamsService.GetAllAsync()", source, StringComparison.Ordinal);
        Assert.Contains("UsersService.GetAllAsync()", source, StringComparison.Ordinal);
        Assert.Contains("EntitiesService.GetAllAsync(\"application\", true)", source, StringComparison.Ordinal);

        // Hosts: a single Sieve-filtered call naming exactly the ids on the page.
        Assert.Contains("\"id==\" + string.Join(\"|\", hostIds)", source, StringComparison.Ordinal);

        // And nothing per row: the per-id endpoints must not be reachable from the grid's columns.
        Assert.DoesNotContain("HostsService.GetOne", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TeamsService.GetById", source, StringComparison.Ordinal);
    }

    private static IEnumerable<string> CodeBehindFiles() =>
        Directory.EnumerateFiles(GuiClientSourceRoot(), "*.axaml.cs", SearchOption.AllDirectories);

    private static string GuiClientSourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory != null;
             directory = directory.Parent)
        {
            var project = Path.Combine(directory.FullName, "GUIClient", "GUIClient.csproj");

            if (File.Exists(project))
                return Path.GetDirectoryName(project)!;
        }

        throw new InvalidOperationException(
            $"Could not find GUIClient/GUIClient.csproj walking up from {AppContext.BaseDirectory}.");
    }
}
