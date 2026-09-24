using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Entities;
using DAL.Enums;
using GUIClient.Tools;
using Xunit;

namespace GUIClient.Tests.Tools;

/// <summary>
/// The Posture providers log had to be refreshed by hand before a run the operator had just started
/// showed up in it. These cover the placeholder that fixes that, and the two ways it could have made
/// things worse instead: showing the run twice, and losing the progress panel's selection.
/// </summary>
public class IntegrationSyncLogFeedTest
{
    private static IntegrationSyncLog Row(int id, IntegrationKind kind, int connectionId,
        DateTime startedAt, IntegrationSyncStatus status = IntegrationSyncStatus.Succeeded) => new()
    {
        Id = id,
        Integration = kind,
        ConnectionId = connectionId,
        ConnectionName = "Trend",
        StartedAt = startedAt,
        Status = status
    };

    [Fact]
    public void Pending_Run_Is_Listed_First_And_Marked_Running()
    {
        var history = new List<IntegrationSyncLog>
        {
            Row(1, IntegrationKind.TrendMicroVisionOne, 7, new DateTime(2026, 9, 24, 10, 0, 0))
        };

        var pending = IntegrationSyncLogFeed.Pending(IntegrationKind.TrendMicroVisionOne, 7, "Trend",
            new DateTime(2026, 9, 24, 16, 27, 43));

        var merged = IntegrationSyncLogFeed.Merge(history, [], pending);

        Assert.Equal(2, merged.Count);
        Assert.Same(pending, merged[0]);
        Assert.Equal(IntegrationSyncStatus.Running, merged[0].Status);
        Assert.Equal(0, merged[0].Id);
    }

    [Fact]
    public void Server_Running_Row_Replaces_The_Placeholder_Rather_Than_Joining_It()
    {
        var pending = IntegrationSyncLogFeed.Pending(IntegrationKind.TrendMicroVisionOne, 7, "Trend",
            new DateTime(2026, 9, 24, 16, 27, 43));

        var server = new List<IntegrationSyncLog>
        {
            Row(9, IntegrationKind.TrendMicroVisionOne, 7, new DateTime(2026, 9, 24, 16, 27, 44),
                IntegrationSyncStatus.Running)
        };

        var merged = IntegrationSyncLogFeed.Merge(server, [], pending);

        Assert.Single(merged);
        Assert.Equal(9, merged[0].Id);
    }

    [Fact]
    public void A_Running_Row_For_Another_Connection_Does_Not_Hide_The_Placeholder()
    {
        var pending = IntegrationSyncLogFeed.Pending(IntegrationKind.TrendMicroVisionOne, 7, "Trend",
            new DateTime(2026, 9, 24, 16, 27, 43));

        var server = new List<IntegrationSyncLog>
        {
            Row(9, IntegrationKind.TrendMicroVisionOne, 8, new DateTime(2026, 9, 24, 16, 0, 0),
                IntegrationSyncStatus.Running),
            Row(10, IntegrationKind.SecurityScorecard, 7, new DateTime(2026, 9, 24, 15, 0, 0),
                IntegrationSyncStatus.Running)
        };

        var merged = IntegrationSyncLogFeed.Merge(server, [], pending);

        Assert.Equal(3, merged.Count);
        Assert.Contains(merged, r => r.Id == 0);
    }

    [Fact]
    public void A_Finished_Row_For_The_Same_Connection_Does_Not_Hide_The_Placeholder()
    {
        var pending = IntegrationSyncLogFeed.Pending(IntegrationKind.TrendMicroVisionOne, 7, "Trend",
            new DateTime(2026, 9, 24, 16, 27, 43));

        var server = new List<IntegrationSyncLog>
        {
            Row(9, IntegrationKind.TrendMicroVisionOne, 7, new DateTime(2026, 9, 24, 16, 20, 0),
                IntegrationSyncStatus.Failed)
        };

        var merged = IntegrationSyncLogFeed.Merge(server, [], pending);

        Assert.Equal(2, merged.Count);
        Assert.Same(pending, merged[0]);
    }

    [Fact]
    public void Both_Providers_Are_Interleaved_Newest_First()
    {
        var trendMicro = new List<IntegrationSyncLog>
        {
            Row(1, IntegrationKind.TrendMicroVisionOne, 7, new DateTime(2026, 9, 24, 10, 0, 0)),
            Row(2, IntegrationKind.TrendMicroVisionOne, 7, new DateTime(2026, 9, 22, 10, 0, 0))
        };

        var scorecard = new List<IntegrationSyncLog>
        {
            Row(3, IntegrationKind.SecurityScorecard, 4, new DateTime(2026, 9, 23, 10, 0, 0))
        };

        var merged = IntegrationSyncLogFeed.Merge(trendMicro, scorecard);

        Assert.Equal([1, 3, 2], merged.Select(r => r.Id).ToArray());
    }

    [Fact]
    public void Merge_Without_A_Pending_Run_Adds_Nothing()
    {
        var merged = IntegrationSyncLogFeed.Merge(null, null);

        Assert.Empty(merged);
    }

    [Fact]
    public void Reselect_Follows_A_Persisted_Row_Across_A_Refresh()
    {
        var before = Row(9, IntegrationKind.TrendMicroVisionOne, 7, new DateTime(2026, 9, 24, 16, 0, 0),
            IntegrationSyncStatus.Running);

        var after = new List<IntegrationSyncLog>
        {
            Row(9, IntegrationKind.TrendMicroVisionOne, 7, new DateTime(2026, 9, 24, 16, 0, 0),
                IntegrationSyncStatus.Succeeded)
        };

        var selected = IntegrationSyncLogFeed.Reselect(after, before);

        Assert.NotNull(selected);
        Assert.Equal(IntegrationSyncStatus.Succeeded, selected!.Status);
    }

    [Fact]
    public void Reselect_Moves_From_The_Placeholder_To_The_Servers_Row_For_The_Same_Run()
    {
        var pending = IntegrationSyncLogFeed.Pending(IntegrationKind.TrendMicroVisionOne, 7, "Trend",
            new DateTime(2026, 9, 24, 16, 27, 43));

        var after = new List<IntegrationSyncLog>
        {
            Row(9, IntegrationKind.TrendMicroVisionOne, 7, new DateTime(2026, 9, 24, 16, 27, 44),
                IntegrationSyncStatus.Running),
            Row(1, IntegrationKind.TrendMicroVisionOne, 7, new DateTime(2026, 9, 20, 10, 0, 0))
        };

        var selected = IntegrationSyncLogFeed.Reselect(after, pending);

        Assert.NotNull(selected);
        Assert.Equal(9, selected!.Id);
    }

    [Fact]
    public void Reselect_Returns_Null_When_The_Selected_Row_Is_Gone()
    {
        var before = Row(9, IntegrationKind.TrendMicroVisionOne, 7, new DateTime(2026, 9, 24, 16, 0, 0));

        Assert.Null(IntegrationSyncLogFeed.Reselect([], before));
        Assert.Null(IntegrationSyncLogFeed.Reselect([], null));
    }
}
