using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DAL.Entities;
using FluentEmail.Core;
using FluentEmail.Core.Models;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;
using ILogger = Serilog.ILogger;

namespace ServerServices.Tests.ServiceTests;

public class ScheduledReportJobInMemoryTest : InMemoryServiceTestBase
{
    private readonly IQuestPdfRenderingService _renderingService;
    private readonly IDalService _dalService;

    public ScheduledReportJobInMemoryTest()
    {
        _renderingService = GetService<IQuestPdfRenderingService>();
        _dalService = GetService<IDalService>();
    }

    [Fact]
    public async Task TestScheduledReportJob_SuccessFlow()
    {
        // Mock FluentEmail using NSubstitute
        var mockFluentEmail = Substitute.For<IFluentEmail>();
        mockFluentEmail.To(Arg.Any<string>()).Returns(mockFluentEmail);
        mockFluentEmail.Subject(Arg.Any<string>()).Returns(mockFluentEmail);
        mockFluentEmail.Body(Arg.Any<string>()).Returns(mockFluentEmail);
        mockFluentEmail.Attach(Arg.Any<FluentEmail.Core.Models.Attachment>()).Returns(mockFluentEmail);
        mockFluentEmail.SendAsync(Arg.Any<CancellationToken?>()).Returns(Task.FromResult(new SendResponse()));

        // Setup Seed Data
        Seed(ctx =>
        {
            var user = new User 
            { 
                Value = 1, 
                Name = "Admin", 
                Email = "admin@netrisk.io", 
                Type = "Internal",
                Enabled = true,
                Password = new byte[16]
            };
            ctx.Users.Add(user);

            var template = new ReportTemplate { Id = 1, Name = "Monthly Risk Summary", OwnerId = 1 };
            ctx.ReportTemplates.Add(template);

            var version = new ReportTemplateVersion
            {
                Id = 1,
                TemplateId = 1,
                Version = 1,
                LayoutJson = "{}",
                BrandingJson = "{}",
                CreatedAt = DateTime.UtcNow
            };
            ctx.ReportTemplateVersions.Add(version);

            var schedule = new ReportSchedule
            {
                Id = 1,
                ReportTemplateVersionId = 1,
                FrequencyCron = "0 9 1 * *",
                RecipientsJson = "[\"recipient@netrisk.io\"]",
                IsEnabled = true,
                LastStatus = "Pending"
            };
            ctx.ReportSchedules.Add(schedule);
        });

        // Construct Job
        var mockLogger = Substitute.For<ILogger>();
        var job = new ScheduledReportJob(_dalService, _renderingService, mockFluentEmail);

        // Run Job
        await job.Run(1);

        // Assert Status Update in Database
        using var checkCtx = _dalService.GetContext();
        var scheduleDb = await checkCtx.ReportSchedules.FindAsync(1);
        Assert.NotNull(scheduleDb);
        Assert.Equal("Success", scheduleDb.LastStatus);
        Assert.NotNull(scheduleDb.LastRunAt);

        // Verify Email dispatching using Received constraint
        mockFluentEmail.Received(1).To("recipient@netrisk.io");
        mockFluentEmail.Received(1).Subject(Arg.Any<string>());
        mockFluentEmail.Received(1).Attach(Arg.Any<FluentEmail.Core.Models.Attachment>());
        await mockFluentEmail.Received(1).SendAsync(Arg.Any<CancellationToken?>());
    }

    [Fact]
    public async Task TestScheduledReportJob_DisabledSchedule_Skipped()
    {
        var mockFluentEmail = Substitute.For<IFluentEmail>();
        Seed(ctx =>
        {
            var user = new User 
            { 
                Value = 2, 
                Name = "Operator", 
                Email = "operator@netrisk.io", 
                Type = "Internal",
                Enabled = true,
                Password = new byte[16]
            };
            ctx.Users.Add(user);

            var template = new ReportTemplate { Id = 2, Name = "Weekly Incidents Report", OwnerId = 2 };
            ctx.ReportTemplates.Add(template);

            var version = new ReportTemplateVersion
            {
                Id = 2,
                TemplateId = 2,
                Version = 1,
                LayoutJson = "{}",
                BrandingJson = "{}",
                CreatedAt = DateTime.UtcNow
            };
            ctx.ReportTemplateVersions.Add(version);

            var schedule = new ReportSchedule
            {
                Id = 2,
                ReportTemplateVersionId = 2,
                FrequencyCron = "0 9 * * 1",
                RecipientsJson = "[\"recipient2@netrisk.io\"]",
                IsEnabled = false, // DISABLED!
                LastStatus = "Pending"
            };
            ctx.ReportSchedules.Add(schedule);
        });

        var job = new ScheduledReportJob(_dalService, _renderingService, mockFluentEmail);

        await job.Run(2);

        using var checkCtx = _dalService.GetContext();
        var scheduleDb = await checkCtx.ReportSchedules.FindAsync(2);
        Assert.NotNull(scheduleDb);
        Assert.Equal("Pending", scheduleDb.LastStatus); // unchanged
        Assert.Null(scheduleDb.LastRunAt); // skipped

        await mockFluentEmail.DidNotReceive().SendAsync(Arg.Any<CancellationToken?>());
    }
}
