using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Model.DTO;
using Model.Exceptions;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

/// <summary>
/// S55 T354: an incident timeline entry is appended as the caller, now, text only; the incident itself is not touched; an
/// incident outside the caller's entities is not found; empty, over-long or control-laden text is refused, never cut.
/// </summary>
[TestSubject(typeof(IncidentsService))]
public class IncidentTimelineInMemoryTest : InMemoryServiceTestBase
{
    private const int EntityA = 100;
    private const int EntityB = 200;
    private static readonly DateTime LastUpdate = new(2026, 4, 2, 10, 0, 0);
    private static readonly User Analyst = new() { Value = 7, Name = "Analyst", Type = "local", Admin = false, Lang = "en" };

    private readonly IIncidentsService _svc;

    public IncidentTimelineInMemoryTest()
    {
        _svc = GetService<IIncidentsService>();
        SeedUnscoped(ctx =>
        {
            ctx.Entities.Add(NewEntity(EntityA));
            ctx.Entities.Add(NewEntity(EntityB));
            ctx.Incidents.Add(new Incident
            {
                Id = 1, Name = "I1", Description = "d", EntityId = EntityA, Status = 1, LastUpdate = LastUpdate, UpdatedById = 3
            });
            ctx.Incidents.Add(new Incident { Id = 2, Name = "I2", Description = "d", EntityId = EntityB, Status = 1 });
        });
    }

    private static Entity NewEntity(int id) => new()
    {
        Id = id, DefinitionName = "organizationUnit", DefinitionVersion = "1", Status = "active",
        Created = DateTime.Now, Updated = DateTime.Now
    };

    [Fact]
    public async Task TestAnEntryIsAppendedAsTheCallerNowAndTheIncidentIsNotTouched()
    {
        var before = DateTime.Now.AddSeconds(-1);

        var entry = await _svc.AddTimelineEntryAsync(1, "  Contained at the mail gateway  ", Analyst);

        Assert.True(entry.Id > 0);
        Assert.Equal(1, entry.IncidentId);
        Assert.Equal(Analyst.Value, entry.UserId);
        Assert.Equal("Contained at the mail gateway", entry.Text);
        Assert.True(entry.At >= before);

        await using var ctx = OpenContext();
        var stored = Assert.Single(ctx.NrActions.Where(a => a.IncidentId == 1));
        Assert.Equal(nameof(Incident), stored.ObjectType);
        Assert.Equal(Analyst.Value, stored.UserId);
        Assert.Equal("Contained at the mail gateway", stored.Message);
        var incident = ctx.Incidents.Single(i => i.Id == 1);
        Assert.Equal(LastUpdate, incident.LastUpdate);   // append-only: the incident's own row is unchanged
        Assert.Equal(3, incident.UpdatedById);
    }

    [Fact]
    public async Task TestEntriesAccumulateAndNeverReplaceEachOther()
    {
        await _svc.AddTimelineEntryAsync(1, "first", Analyst);
        await _svc.AddTimelineEntryAsync(1, "second", Analyst);

        await using var ctx = OpenContext();
        Assert.Equal(new[] { "first", "second" },
            ctx.NrActions.Where(a => a.IncidentId == 1).OrderBy(a => a.Id).Select(a => a.Message).ToArray());
    }

    [Fact]
    public async Task TestAnUnknownIncidentIsNotFoundAndNothingIsStored()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.AddTimelineEntryAsync(99, "text", Analyst));

        await using var ctx = OpenContext();
        Assert.Empty(ctx.NrActions);
    }

    [Fact]
    public async Task TestAnIncidentOutsideTheCallersEntitiesIsNotFoundAndNothingIsStored()
    {
        ScopeTo(EntityA);

        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.AddTimelineEntryAsync(2, "text", Analyst));
        Assert.Equal(1, (await _svc.AddTimelineEntryAsync(1, "in scope", Analyst)).IncidentId);

        ScopeToEverything();
        await using var ctx = OpenContext();
        Assert.Empty(ctx.NrActions.Where(a => a.IncidentId == 2));
    }

    [Fact]
    public async Task TestDeletingAnIncidentDeletesItsTimelineEntriesInTheSameSave()
    {
        // fk_inc_actions has no ON DELETE (RESTRICT): an entry left behind would make the incident undeletable in MariaDB.
        // The in-memory store does not enforce it, so the rows are counted (DAL.IntegrationTests has the enforced version).
        await _svc.AddTimelineEntryAsync(1, "first", Analyst);
        await _svc.AddTimelineEntryAsync(1, "second", Analyst);
        await _svc.AddTimelineEntryAsync(2, "other incident", Analyst);
        var savesBefore = SaveChangesCount;

        await _svc.DeleteByIdAsync(1);

        Assert.Equal(savesBefore + 1, SaveChangesCount);
        await using var ctx = OpenContext();
        Assert.False(ctx.Incidents.Any(i => i.Id == 1));
        // Deleted, not merely unlinked (EF's default for a tracked optional dependent is to null its IncidentId).
        Assert.Equal(new[] { "other incident" }, ctx.NrActions.Select(a => a.Message).ToArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bell\u0007")]
    [InlineData("nul\u0000here")]
    public async Task TestEmptyOrControlLadenTextIsRefused(string? text)
    {
        var error = await Assert.ThrowsAsync<InvalidParameterException>(() => _svc.AddTimelineEntryAsync(1, text, Analyst));

        Assert.Equal("text", error.ParameterName);
        await using var ctx = OpenContext();
        Assert.Empty(ctx.NrActions);
    }

    [Fact]
    public async Task TestTextOverTheColumnBoundIsRefusedNotCut()
    {
        var longest = new string('a', IncidentTimelineEntryDto.MaxTextLength);

        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.AddTimelineEntryAsync(1, longest + "b", Analyst));
        Assert.Equal(longest, (await _svc.AddTimelineEntryAsync(1, longest, Analyst)).Text);
        Assert.Equal("multi\nline\ttext", (await _svc.AddTimelineEntryAsync(1, "multi\nline\ttext", Analyst)).Text);
    }
}
