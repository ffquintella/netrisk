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

    [Fact]
    public async Task TestTriggerTestReturnsNotFoundForAnUnknownId()
    {
        var result = await _controller.TriggerTest(999);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("Report schedule with ID 999 not found", notFound.Value);
    }
}
