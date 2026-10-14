using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using DAL.Entities;
using FluentEmail.Core;
using FluentEmail.Core.Models;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using ServerServices.Interfaces;
using ServerServices.Security;
using ServerServices.Services;
using ServerServices.Tests.Mock;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Security;

/// <summary>
/// The review's blocker on T237. The job host's principal is unrestricted, and <see cref="ScheduledReportJob"/> read the
/// report's incidents under it — so any user who could save a schedule (any valid user, with recipients of their
/// choosing) was mailed the newest incidents of <b>every</b> entity. The job now reads under the schedule creator's scope
/// as it stands when the job runs: hierarchical, and nothing for a creator who is gone, disabled, locked out, without the
/// <c>reports</c> permission or without any entity role.
///
/// The DAL here is the production <see cref="DalService"/> on an in-memory store (<see cref="PrincipalDalService"/>),
/// acting as the job host's principal (<c>scope=global</c>), so the test is the job as the host runs it.
/// </summary>
[TestSubject(typeof(ScheduledReportJob))]
public class ScheduledReportScopeTest : InMemoryServiceTestBase
{
    private const int Org = 1, UnitA = 10, ProcessA = 11, UnitB = 20;

    private const int ScopedCreator = 5, DisabledCreator = 6, CreatorWithoutReports = 7, AdminCreator = 8,
        RevokedCreator = 9, LockedCreator = 4;

    private const int ReportsRole = 70, PlainRole = 71;

    [ThreadStatic] private static PrincipalDalService? _building;

    private readonly PrincipalDalService _dal;
    private readonly IQuestPdfRenderingService _renderer = Substitute.For<IQuestPdfRenderingService>();
    private readonly IFluentEmail _email = Substitute.For<IFluentEmail>();
    private List<Incident>? _rendered;

    public ScheduledReportScopeTest() : base(name => _building = new PrincipalDalService(name, new FixedHttpContextAccessor()))
    {
        _dal = _building!;

        // The job host's principal (T237): what every context the job opens on its own is scoped to.
        _dal.ActAs(new Claim(BackgroundServiceHttpContextAccessor.ScopeClaimType, BackgroundServiceHttpContextAccessor.GlobalScope));

        _renderer.RenderFromTemplateAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Do<IEnumerable<Incident>>(incidents => _rendered = incidents.ToList()), Arg.Any<string>())
            .Returns(new byte[] { 1, 2, 3 });

        _email.To(Arg.Any<string>()).Returns(_email);
        _email.Subject(Arg.Any<string>()).Returns(_email);
        _email.Body(Arg.Any<string>()).Returns(_email);
        _email.Attach(Arg.Any<Attachment>()).Returns(_email);
        _email.SendAsync(Arg.Any<CancellationToken?>()).Returns(Task.FromResult(new SendResponse()));

        using var db = _dal.GetContext();

        db.Entities.AddRange(NewEntity(Org, null), NewEntity(UnitA, Org), NewEntity(ProcessA, UnitA), NewEntity(UnitB, Org));

        var reports = new Permission { Id = 43, Key = ScheduledReportJob.RequiredPermission, Name = "reports", Description = "" };
        db.Permissions.Add(reports);
        db.Roles.Add(new Role { Value = ReportsRole, Name = "Reporter", Permissions = [reports] });
        db.Roles.Add(new Role { Value = PlainRole, Name = "Plain" });

        db.Users.AddRange(
            NewUser(ScopedCreator, ReportsRole),
            NewUser(DisabledCreator, ReportsRole, enabled: false),
            NewUser(LockedCreator, ReportsRole, lockout: 1),
            NewUser(CreatorWithoutReports, PlainRole),
            NewUser(AdminCreator, PlainRole, admin: true),
            NewUser(RevokedCreator, ReportsRole));

        foreach (var user in new[] { ScopedCreator, DisabledCreator, LockedCreator, CreatorWithoutReports })
            db.UserEntityRoles.Add(new UserEntityRole { UserId = user, EntityId = UnitA, RoleId = ReportsRole, CreatedAt = DateTime.UtcNow });
        db.UserEntityRoles.Add(new UserEntityRole
        {
            UserId = RevokedCreator, EntityId = UnitA, RoleId = ReportsRole, CreatedAt = DateTime.UtcNow,
            RevokedAt = DateTime.UtcNow
        });

        db.Incidents.AddRange(
            NewIncident(101, UnitA, "unit A outage"),
            NewIncident(102, ProcessA, "process A breach"),
            NewIncident(103, UnitB, "unit B breach"),
            NewIncident(104, null, "unassigned incident"));

        db.ReportTemplates.Add(new ReportTemplate { Id = 1, Name = "Incidents", OwnerId = AdminCreator });
        db.ReportTemplateVersions.Add(new ReportTemplateVersion
        {
            Id = 1, TemplateId = 1, Version = 1, LayoutJson = "{}", BrandingJson = "{}", CreatedAt = DateTime.UtcNow
        });

        db.SaveChanges();
    }

    private static Entity NewEntity(int id, int? parent) => new()
    {
        Id = id, DefinitionName = "organizationUnit", DefinitionVersion = "2.6", Status = "active", Parent = parent,
        Created = DateTime.UtcNow, Updated = DateTime.UtcNow
    };

    private static User NewUser(int id, int role, bool enabled = true, sbyte lockout = 0, bool admin = false) => new()
    {
        Value = id, Name = $"user{id}", Login = $"user{id}", Email = $"user{id}@x.test", Type = "local",
        Enabled = enabled, Lockout = lockout, Admin = admin, RoleId = role, Password = new byte[16]
    };

    private static Incident NewIncident(int id, int? entityId, string description) => new()
    {
        Id = id, Description = description, CreatedById = AdminCreator, EntityId = entityId
    };

    private async Task<ReportSchedule> RunScheduleOf(int? creator)
    {
        int id;
        await using (var db = _dal.GetContext())
        {
            var schedule = new ReportSchedule
            {
                ReportTemplateVersionId = 1, FrequencyCron = "0 9 * * *", Timezone = "UTC",
                RecipientsJson = "[\"outside@attacker.test\"]", IsEnabled = true, LastStatus = "Created",
                CreatedById = creator
            };
            db.ReportSchedules.Add(schedule);
            await db.SaveChangesAsync();
            id = schedule.Id;
        }

        var job = new ScheduledReportJob(_dal, _renderer, _email,
            new UserEntityScopeResolver(_dal, GetService<IPermissionsService>()));

        await job.Run(id);

        await using var read = _dal.GetContext();
        return await read.ReportSchedules.AsNoTracking().SingleAsync(s => s.Id == id);
    }

    /// <summary>The regression: a scoped user's schedule never renders another entity's incidents.</summary>
    [Fact]
    public async Task TestAScopedCreatorsScheduleRendersOnlyTheirSubtreesIncidents()
    {
        var schedule = await RunScheduleOf(ScopedCreator);

        Assert.Equal("Success", schedule.LastStatus);
        Assert.NotNull(_rendered);
        // Their unit and the process below it (T292), never the sibling unit nor an unassigned incident.
        Assert.Equal(new[] { 101, 102 }, _rendered!.Select(i => i.Id).OrderBy(i => i));
        Assert.DoesNotContain(_rendered, i => i.EntityId == UnitB || i.EntityId == null);
        await _email.Received(1).SendAsync(Arg.Any<CancellationToken?>());
    }

    [Theory]
    [InlineData(DisabledCreator)]
    [InlineData(LockedCreator)]
    [InlineData(CreatorWithoutReports)]
    public async Task TestACreatorWhoMayNoLongerActGetsNothingRenderedOrSent(int creator)
    {
        var schedule = await RunScheduleOf(creator);

        Assert.Equal(ScheduledReportJob.SkippedNoOwnerStatus, schedule.LastStatus);
        Assert.NotNull(schedule.LastRunAt);
        Assert.Null(_rendered);
        await _email.DidNotReceive().SendAsync(Arg.Any<CancellationToken?>());
    }

    /// <summary>A schedule from before the creator column, or whose creator was deleted, runs for nobody.</summary>
    [Fact]
    public async Task TestAScheduleWithNoCreatorGetsNothingRenderedOrSent()
    {
        var schedule = await RunScheduleOf(null);

        Assert.Equal(ScheduledReportJob.SkippedNoOwnerStatus, schedule.LastStatus);
        Assert.Null(_rendered);
        await _email.DidNotReceive().SendAsync(Arg.Any<CancellationToken?>());
    }

    [Fact]
    public async Task TestAnUnknownCreatorGetsNothingRenderedOrSent()
    {
        var schedule = await RunScheduleOf(999);

        Assert.Equal(ScheduledReportJob.SkippedNoOwnerStatus, schedule.LastStatus);
        Assert.Null(_rendered);
    }

    /// <summary>The scope is the creator's as it is <b>now</b>: an entity role revoked since the schedule was saved reads nothing.</summary>
    [Fact]
    public async Task TestACreatorWhoseEntityRolesWereRevokedRendersNoIncidents()
    {
        var schedule = await RunScheduleOf(RevokedCreator);

        Assert.NotNull(_rendered);
        Assert.Empty(_rendered!);
        Assert.Equal("Success", schedule.LastStatus);
    }

    [Fact]
    public async Task TestAnAdministratorsScheduleReadsEveryEntity()
    {
        await RunScheduleOf(AdminCreator);

        Assert.Equal(new[] { 101, 102, 103, 104 }, _rendered!.Select(i => i.Id).OrderBy(i => i));
    }
}
