using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.Mock;
using DAL.Entities;
using Hangfire;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ServerServices.Services;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// The controller talks to the database directly and to Hangfire through its static
/// <c>RecurringJob</c>/<c>BackgroundJob</c> facades. The database is an
/// <see cref="InMemoryDalService"/> unique to each test instance; Hangfire gets a substituted
/// <see cref="JobStorage"/> so registering or enqueueing a job stays inside the process.
/// </summary>
[TestSubject(typeof(ReportSchedulesController))]
public class ReportSchedulesControllerTest : BaseControllerTest
{
    private static readonly DateTime Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <remarks>
    /// <c>JobStorage.Current</c> is process-wide and Hangfire's static facades resolve it lazily and
    /// only once, so it is set here rather than per test.
    /// </remarks>
    static ReportSchedulesControllerTest()
    {
        JobStorage.Current = Substitute.For<JobStorage>();
    }

    private readonly InMemoryDalService _dalService = new InMemoryDalService(Guid.NewGuid().ToString());
    private readonly ReportSchedulesController _controller;

    public ReportSchedulesControllerTest()
    {
        Seed();

        _controller = ResolveController<ReportSchedulesController>(
            services => services.AddSingleton<IDalService>(_dalService));
    }

    /// <summary>Schedule 1 is enabled, schedule 2 is not, so both update branches have a subject.</summary>
    private void Seed()
    {
        using var context = _dalService.GetContext();

        context.ReportTemplates.Add(new ReportTemplate
        {
            Id = 1,
            Name = "Quarterly Risk Report",
            OwnerId = 1,
            CreatedAt = Timestamp,
            UpdatedAt = Timestamp
        });

        context.ReportTemplateVersions.Add(new ReportTemplateVersion
        {
            Id = 1,
            TemplateId = 1,
            Version = 1,
            LayoutJson = "{}",
            BrandingJson = "{}",
            CreatedAt = Timestamp
        });

        context.ReportSchedules.Add(new ReportSchedule
        {
            Id = 1,
            ReportTemplateVersionId = 1,
            FrequencyCron = "0 0 * * *",
            Timezone = "UTC",
            RecipientsJson = "[\"board@test.com\"]",
            IsEnabled = true,
            LastStatus = "Created"
        });

        context.ReportSchedules.Add(new ReportSchedule
        {
            Id = 2,
            ReportTemplateVersionId = 1,
            FrequencyCron = "0 6 * * *",
            Timezone = "UTC",
            RecipientsJson = "[]",
            IsEnabled = false,
            LastStatus = "Created"
        });

        context.SaveChanges();
    }

    [Fact]
    public async Task TestGetAllReturnsEverySchedule()
    {
        var result = await _controller.GetAll();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var schedules = Assert.IsType<List<ReportSchedule>>(ok.Value);

        Assert.Equal(2, schedules.Count);
        Assert.Contains(schedules, s => s.FrequencyCron == "0 0 * * *");
    }

    [Fact]
    public async Task TestGetByIdReturnsTheSchedule()
    {
        var result = await _controller.GetById(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var schedule = Assert.IsType<ReportSchedule>(ok.Value);

        Assert.Equal(1, schedule.Id);
        Assert.Equal("[\"board@test.com\"]", schedule.RecipientsJson);
        Assert.True(schedule.IsEnabled);
    }

    [Fact]
    public async Task TestGetByIdReturnsNotFoundForAnUnknownId()
    {
        var result = await _controller.GetById(999);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal("Report schedule with ID 999 not found", notFound.Value);
    }

    [Fact]
    public async Task TestCreateStoresADisabledScheduleWithoutRegisteringAJob()
    {
        var request = new CreateScheduleRequest
        {
            ReportTemplateVersionId = 1,
            FrequencyCron = "0 3 * * *",
            Timezone = "UTC",
            RecipientsJson = "[\"ciso@test.com\"]",
            IsEnabled = false
        };

        var result = await _controller.Create(request);

        var created = Assert.IsType<CreatedResult>(result.Result);
        var schedule = Assert.IsType<ReportSchedule>(created.Value);

        Assert.Equal("0 3 * * *", schedule.FrequencyCron);
        Assert.Equal("Created", schedule.LastStatus);
        Assert.Null(schedule.LastRunAt);
        Assert.False(schedule.IsEnabled);
        Assert.Equal($"ReportSchedules/{schedule.Id}", created.Location);

        using var context = _dalService.GetContext();
        Assert.Equal(3, context.ReportSchedules.ToList().Count);
    }

    [Fact]
    public async Task TestCreateAnEnabledScheduleRegistersTheRecurringJob()
    {
        var request = new CreateScheduleRequest
        {
            ReportTemplateVersionId = 1,
            FrequencyCron = "0 4 * * *",
            Timezone = "UTC",
            RecipientsJson = "[\"ciso@test.com\"]",
            IsEnabled = true
        };

        var result = await _controller.Create(request);

        var created = Assert.IsType<CreatedResult>(result.Result);
        var schedule = Assert.IsType<ReportSchedule>(created.Value);

        Assert.True(schedule.IsEnabled);
        Assert.True(schedule.Id > 0);
    }

    [Fact]
    public async Task TestUpdateOfAnEnabledScheduleSavesTheNewCron()
    {
        var request = new UpdateScheduleRequest
        {
            ReportTemplateVersionId = 1,
            FrequencyCron = "30 5 * * *",
            Timezone = "UTC",
            RecipientsJson = "[\"audit@test.com\"]",
            IsEnabled = true
        };

        var result = await _controller.Update(1, request);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var schedule = Assert.IsType<ReportSchedule>(ok.Value);

        Assert.Equal("30 5 * * *", schedule.FrequencyCron);
        Assert.Equal("[\"audit@test.com\"]", schedule.RecipientsJson);

        using var context = _dalService.GetContext();
        var stored = Assert.Single(context.ReportSchedules.Where(s => s.Id == 1).ToList());
        Assert.Equal("30 5 * * *", stored.FrequencyCron);
        Assert.True(stored.IsEnabled);
    }

    [Fact]
    public async Task TestUpdateDisablingAScheduleRemovesTheRecurringJob()
    {
        var request = new UpdateScheduleRequest
        {
            ReportTemplateVersionId = 1,
            FrequencyCron = "0 0 * * *",
            Timezone = "UTC",
            RecipientsJson = "[]",
            IsEnabled = false
        };

        var result = await _controller.Update(1, request);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var schedule = Assert.IsType<ReportSchedule>(ok.Value);

        Assert.False(schedule.IsEnabled);

        using var context = _dalService.GetContext();
        var stored = Assert.Single(context.ReportSchedules.Where(s => s.Id == 1).ToList());
        Assert.False(stored.IsEnabled);
    }

    [Fact]
    public async Task TestUpdateReturnsNotFoundForAnUnknownId()
    {
        var request = new UpdateScheduleRequest
        {
            ReportTemplateVersionId = 1,
            FrequencyCron = "0 0 * * *",
            Timezone = "UTC",
            RecipientsJson = "[]",
            IsEnabled = false
        };

        var result = await _controller.Update(999, request);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal("Report schedule with ID 999 not found", notFound.Value);
    }

    [Fact]
    public async Task TestDeleteRemovesTheSchedule()
    {
        var result = await _controller.Delete(2);

        Assert.IsType<NoContentResult>(result);

        using var context = _dalService.GetContext();
        Assert.Empty(context.ReportSchedules.Where(s => s.Id == 2).ToList());
    }

    [Fact]
    public async Task TestDeleteReturnsNotFoundForAnUnknownId()
    {
        var result = await _controller.Delete(999);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("Report schedule with ID 999 not found", notFound.Value);
    }

    [Fact]
    public async Task TestTriggerTestEnqueuesAnImmediateRun()
    {
        var result = await _controller.TriggerTest(1);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Report test enqueued successfully", ok.Value);
    }

    // ------------------------------------------------------------------ ownership (T237 review)

    /// <summary>The caller every test here acts as (<c>MockedUsersService</c>).</summary>
    private const int Caller = 1;

    /// <summary>Schedules 3 (the caller's) and 4 (another user's), and a caller scoped to one entity.</summary>
    private void SeedOwnedSchedulesAndScopeTheCaller()
    {
        using (var context = _dalService.GetContext())
        {
            context.ReportSchedules.Add(new ReportSchedule
            {
                Id = 3, ReportTemplateVersionId = 1, FrequencyCron = "0 7 * * *", Timezone = "UTC",
                RecipientsJson = "[\"me@test.com\"]", IsEnabled = false, LastStatus = "Created", CreatedById = Caller
            });
            context.ReportSchedules.Add(new ReportSchedule
            {
                Id = 4, ReportTemplateVersionId = 1, FrequencyCron = "0 8 * * *", Timezone = "UTC",
                RecipientsJson = "[\"them@test.com\"]", IsEnabled = true, LastStatus = "Created", CreatedById = 77
            });
            context.SaveChanges();
        }

        _dalService.Scope = DAL.Context.EntityScope.ForEntities([10]);
    }

    private static UpdateScheduleRequest Redirect(string recipients) => new()
    {
        ReportTemplateVersionId = 1, FrequencyCron = "0 0 * * *", Timezone = "UTC", RecipientsJson = recipients,
        IsEnabled = false
    };

    /// <summary>Every action needs <c>reports</c> now; it used to need only a valid user.</summary>
    [Fact]
    public void TestEveryActionRequiresTheReportsPermission()
    {
        var attribute = Assert.Single(typeof(ReportSchedulesController)
            .GetCustomAttributes(typeof(API.Security.PermissionAuthorizeAttribute), inherit: true)
            .Cast<API.Security.PermissionAuthorizeAttribute>());

        Assert.Equal("reports", attribute.Permission);
        Assert.Equal(ScheduledReportJob.RequiredPermission, attribute.Permission);
    }

    /// <summary>The creator is the caller — the job reads under their scope — and the body cannot name another.</summary>
    [Fact]
    public async Task TestCreateStampsTheCallerAsTheCreator()
    {
        var result = await _controller.Create(new CreateScheduleRequest
        {
            ReportTemplateVersionId = 1, FrequencyCron = "0 3 * * *", Timezone = "UTC",
            RecipientsJson = "[\"ciso@test.com\"]", IsEnabled = false
        });

        var schedule = Assert.IsType<ReportSchedule>(Assert.IsType<CreatedResult>(result.Result).Value);
        Assert.Equal(Caller, schedule.CreatedById);
    }

    [Fact]
    public async Task TestAScopedCallerListsOnlyTheirOwnSchedules()
    {
        SeedOwnedSchedulesAndScopeTheCaller();

        var result = await _controller.GetAll();

        var schedules = Assert.IsType<List<ReportSchedule>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(new[] { 3 }, schedules.Select(s => s.Id));
    }

    /// <summary>
    /// Whoever edits the recipients of somebody else's schedule receives that person's entities, so a scoped caller
    /// cannot read, redirect, delete or trigger another user's schedule — nor a creator-less one.
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(1)]
    public async Task TestAScopedCallerCannotTouchAScheduleThatIsNotTheirs(int id)
    {
        SeedOwnedSchedulesAndScopeTheCaller();

        Assert.IsType<NotFoundObjectResult>((await _controller.GetById(id)).Result);
        Assert.IsType<NotFoundObjectResult>((await _controller.Update(id, Redirect("[\"me@test.com\"]"))).Result);
        Assert.IsType<NotFoundObjectResult>(await _controller.TriggerTest(id));
        Assert.IsType<NotFoundObjectResult>(await _controller.Delete(id));

        using var context = _dalService.GetContext();
        var stored = context.ReportSchedules.Single(s => s.Id == id);
        Assert.DoesNotContain("me@test.com", stored.RecipientsJson);
    }

    [Fact]
    public async Task TestAScopedCallerManagesTheirOwnSchedule()
    {
        SeedOwnedSchedulesAndScopeTheCaller();

        Assert.IsType<OkObjectResult>((await _controller.GetById(3)).Result);
        var updated = Assert.IsType<ReportSchedule>(Assert.IsType<OkObjectResult>(
            (await _controller.Update(3, Redirect("[\"new@test.com\"]"))).Result).Value);
        Assert.Equal((Caller, "[\"new@test.com\"]"), (updated.CreatedById, updated.RecipientsJson));
        Assert.IsType<OkObjectResult>(await _controller.TriggerTest(3));
        Assert.IsType<NoContentResult>(await _controller.Delete(3));
    }

    /// <summary>
    /// An unrestricted caller sees and manages every schedule. Saving one from before the creator column adopts it — it
    /// runs for nobody until then — while an existing creator is kept, so an administrator's edit never widens what a
    /// user's schedule reads.
    /// </summary>
    [Fact]
    public async Task TestAnUnrestrictedCallerAdoptsACreatorlessScheduleAndKeepsAnExistingCreator()
    {
        SeedOwnedSchedulesAndScopeTheCaller();
        _dalService.Scope = DAL.Context.EntityScope.Unrestricted;

        var all = Assert.IsType<List<ReportSchedule>>(Assert.IsType<OkObjectResult>((await _controller.GetAll()).Result).Value);
        Assert.Equal(new[] { 1, 2, 3, 4 }, all.Select(s => s.Id).OrderBy(i => i));

        var adopted = Assert.IsType<ReportSchedule>(Assert.IsType<OkObjectResult>(
            (await _controller.Update(2, Redirect("[]"))).Result).Value);
        Assert.Equal(Caller, adopted.CreatedById);

        var kept = Assert.IsType<ReportSchedule>(Assert.IsType<OkObjectResult>(
            (await _controller.Update(4, Redirect("[]"))).Result).Value);
        Assert.Equal(77, kept.CreatedById);
    }

    [Fact]
    public async Task TestTriggerTestReturnsNotFoundForAnUnknownId()
    {
        var result = await _controller.TriggerTest(999);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("Report schedule with ID 999 not found", notFound.Value);
    }
}
