using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Entities;
using DAL.Enums;
using GUIClient.Tools.Hosts;
using JetBrains.Annotations;
using Xunit;

namespace GUIClient.Tests.Tools.Hosts;

/// <summary>
/// The History tab (S38 §3.3, §5.4): one group per save (correlation id), newest first, filterable
/// by field, actor and start date, each change reading <c>Field  old → new</c>.
/// </summary>
[TestSubject(typeof(HostHistory))]
public class HostHistoryTest
{
    private static readonly DateTime T0 = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    private static AuditLog Row(int id, string? correlation, string actor, DateTime at, string field,
        string? oldValue, string? newValue, AuditLogAction action = AuditLogAction.Update) => new()
    {
        Id = id, EntityType = "Host", EntityId = 7, CorrelationId = correlation, Actor = actor,
        OccurredAt = at, Field = field, OldValue = oldValue, NewValue = newValue, Action = action
    };

    private static List<AuditLog> Trail() =>
    [
        Row(1, "save-1", "alice", T0, "Criticality", "3", "5"),
        Row(2, "save-1", "alice", T0, "Owner", null, "Infra"),
        Row(3, "save-2", "Jira Assets import", T0.AddDays(1), "Environment", "Homolog", "Produção"),
        Row(4, null, "bob", T0.AddDays(-2), "HostName", "old", "new"),
        Row(5, null, "bob", T0.AddDays(-3), "Ip", "10.0.0.1", "10.0.0.2"),
        Row(6, "save-0", "system", T0.AddDays(-10), "", null, "{\"HostName\":\"web\"}", AuditLogAction.Create),
    ];

    [Fact]
    public void RowsOfOneSaveFormOneGroupAndGroupsAreNewestFirst()
    {
        var groups = HostHistory.Group(Trail());

        Assert.Equal(new[] { "save-2", "save-1", "row:4", "row:5", "save-0" }, groups.Select(g => g.Key).ToArray());
        Assert.Equal(2, groups[1].Changes.Count);
        Assert.Equal("alice", groups[1].Actor);
    }

    [Fact]
    public void UncorrelatedRowsAreNotMergedWithEachOther()
    {
        var groups = HostHistory.Group(Trail());

        Assert.Single(groups, g => g.Key == "row:4");
        Assert.Single(groups, g => g.Key == "row:5");
    }

    [Fact]
    public void AFieldFilterKeepsOnlyThatFieldAndDropsGroupsLeftEmpty()
    {
        var groups = HostHistory.Group(Trail(), field: "Criticality");

        var group = Assert.Single(groups);
        var change = Assert.Single(group.Changes);
        Assert.Equal("Criticality", change.Field);
    }

    [Fact]
    public void AnActorFilterKeepsWholeSavesByThatActor()
    {
        var groups = HostHistory.Group(Trail(), actor: "bob");

        Assert.Equal(new[] { "row:4", "row:5" }, groups.Select(g => g.Key).ToArray());
    }

    [Fact]
    public void ASinceDateKeepsSavesAtOrAfterIt()
    {
        var groups = HostHistory.Group(Trail(), sinceUtc: T0);

        Assert.Equal(new[] { "save-2", "save-1" }, groups.Select(g => g.Key).ToArray());
    }

    [Fact]
    public void TheFiltersCombine()
    {
        Assert.Empty(HostHistory.Group(Trail(), field: "Criticality", actor: "bob"));
        Assert.Single(HostHistory.Group(Trail(), field: "Environment", sinceUtc: T0));
    }

    [Fact]
    public void TheFilterOptionsAreTheDistinctNamedFieldsAndActors()
    {
        Assert.Equal(new[] { "Criticality", "Environment", "HostName", "Ip", "Owner" }, HostHistory.Fields(Trail()).ToArray());
        Assert.Equal(new[] { "alice", "bob", "Jira Assets import", "system" }, HostHistory.Actors(Trail()).ToArray());
    }

    [Fact]
    public void AnUnspecifiedTimestampIsReadAsUtc()
    {
        var unspecified = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Unspecified);

        Assert.Equal(DateTimeKind.Utc, HostHistory.AsUtc(unspecified).Kind);
        Assert.Equal(unspecified.Ticks, HostHistory.AsUtc(unspecified).Ticks);
    }

    [Fact]
    public void PresentReadsFieldOldArrowNewAndLabelsCreateRowsByAction()
    {
        var entries = HostHistory.Present(
            HostHistory.Group(Trail()),
            key => "[" + key + "]",
            (field, value) => field == "Criticality" && value != null ? "level " + value : value,
            utc => utc.ToString("yyyy-MM-dd"));

        var save1 = entries[1];
        Assert.Equal("alice · 2026-09-30", save1.Header);
        Assert.Contains(new HostHistoryLine("[Criticality]", "level 3 → level 5"), save1.Lines);
        Assert.Contains(new HostHistoryLine("[Owner]", "– → Infra"), save1.Lines);

        var created = entries.Last();
        var line = Assert.Single(created.Lines);
        Assert.Equal("[AuditActionCreate]", line.Label);
        Assert.Equal("{\"HostName\":\"web\"}", line.Detail);
    }

    [Fact]
    public void ADeleteRowShowsTheRecordAsItWas()
    {
        var trail = new[] { Row(1, "x", "alice", T0, "", "{\"HostName\":\"gone\"}", null, AuditLogAction.Delete) };

        var line = HostHistory.Present(HostHistory.Group(trail), k => k, (_, v) => v, _ => "").Single().Lines.Single();

        Assert.Equal("AuditActionDelete", line.Label);
        Assert.Equal("{\"HostName\":\"gone\"}", line.Detail);
    }

    [Fact]
    public void AFieldWithNoLabelReadsAsItsPropertyName()
    {
        var trail = new[] { Row(1, "x", "alice", T0, "SomeFutureColumn", "a", "b") };

        var line = HostHistory.Present(HostHistory.Group(trail), k => "[" + k + "]", (_, v) => v, _ => "").Single().Lines.Single();

        Assert.Equal("SomeFutureColumn", line.Label);
    }

    /// <summary>Resolved at run time, so the literal-key coverage scan cannot see them.</summary>
    [Fact]
    public void EveryFieldAndActionLabelKeyIsDeclaredInAllThreeResourceFiles()
    {
        var keys = HostHistory.AllFieldLabelKeys
            .Concat(new[] { AuditLogAction.Create, AuditLogAction.Update, AuditLogAction.Delete }.Select(HostHistory.ActionLabelKey));

        var missing = HostsTestFiles.MissingFromAnyResource(keys);

        Assert.True(missing.Count == 0, string.Join("\n", missing));
    }
}
