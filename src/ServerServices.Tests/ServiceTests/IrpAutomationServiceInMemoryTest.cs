using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

public class IrpAutomationServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IIncidentsService _incidentsService;
    private readonly IDalService _dalService;
    private readonly IEmailService _mockEmailService;

    public IrpAutomationServiceInMemoryTest()
    {
        // Setup mock email service
        _mockEmailService = Substitute.For<IEmailService>();
        
        // Re-register IIrpAutomationService with mock email service dependency
        var dalService = GetService<IDalService>();
        var localization = GetService<ILocalizationService>();
        var log = Serilog.Log.Logger;
        var automationService = new IrpAutomationService(log, dalService, localization, _mockEmailService);

        // We construct the IncidentsService directly passing our re-registered automation engine
        var filesService = GetService<IFilesService>();
        var plansService = GetService<IIncidentResponsePlansService>();
        _incidentsService = new IncidentsService(log, dalService, filesService, plansService, automationService);
        _dalService = dalService;
    }

    [Fact]
    public async Task TestAutomaticIncidentResponsePlanActivation()
    {
        // 1. Setup Seed Data
        Seed(ctx =>
        {
            var user = new User { Value = 1, Name = "Admin", Email = "admin@netrisk.io", Type = "Internal", Enabled = true, Password = new byte[16] };
            ctx.Users.Add(user);

            // Active IRP Template matching "Ransomware" category
            var template = new IrpTemplate
            {
                Id = 10,
                Name = "Ransomware Response Playbook",
                Description = "Critical path playbook for containment of Ransomware infections",
                MatchingRulesJson = "{\"Category\": \"Ransomware\", \"Status\": 1}",
                IsEnabled = true
            };
            ctx.IrpTemplates.Add(template);

            var task1 = new IrpTemplateTask
                {
                    Id = 101,
                    IrpTemplateId = 10,
                    Title = "Isolate Impacted Subnet",
                    InstructionsMarkdown = "Shutdown switchports and disable active routes for the segment.",
                    AssigneeRuleJson = "{\"Type\": \"User\", \"Value\": \"1\"}",
                    DueOffsetSeconds = 3600, // T+1h
                    RequiresConfirmation = false
                };
            var task2 = new IrpTemplateTask
                {
                    Id = 102,
                    IrpTemplateId = 10,
                    Title = "Approve Backup Restore",
                    InstructionsMarkdown = "Verify backup integrity and trigger restore playbook.",
                    AssigneeRuleJson = "{\"Type\": \"User\", \"Value\": \"1\"}",
                    DueOffsetSeconds = 7200, // T+2h
                    RequiresConfirmation = true // Requires coordinator approval!
                };
            ctx.IrpTemplateTasks.AddRange(task1, task2);
        });

        // 2. Create Incident matching "Ransomware" rule
        var userAdmin = new User { Value = 1 };
        var incident = new Incident
        {
            Description = "Ransomware infection detected on finance workstations",
            Category = "Ransomware",
            Status = 1, // Matches rule Status = 1
            CreatedById = 1
        };

        var createdIncident = await _incidentsService.CreateAsync(incident, userAdmin);

        Assert.NotNull(createdIncident);
        Assert.True(createdIncident.Id > 0);

        // 3. Verify that the matching engine automatically instantiated the IRP
        using var dbContext = _dalService.GetContext();
        
        var generatedPlan = dbContext.IncidentResponsePlans
            .Include(p => p.ActivatedBy)
            .Include(p => p.Tasks)
            .FirstOrDefault(p => p.ActivatedBy.Any(i => i.Id == createdIncident.Id));

        Assert.NotNull(generatedPlan);
        Assert.Equal("Ransomware Response Playbook - Automated Plan", generatedPlan.Name);
        Assert.Equal(2, generatedPlan.Tasks.Count);

        var tasks = generatedPlan.Tasks.OrderBy(t => t.EstimatedDuration).ToList();

        // Verify task 1 ( containment task: Open status, T+1h)
        Assert.Equal("Isolate Impacted Subnet", tasks[0].Name);
        Assert.Equal(0, tasks[0].Status); // 0 = Open
        Assert.Equal(TimeSpan.FromHours(1), tasks[0].EstimatedDuration);

        // Verify task 2 ( restoration task: Proposed status, T+2h)
        Assert.Equal("Approve Backup Restore", tasks[1].Name);
        Assert.Equal(1, tasks[1].Status); // 1 = Proposed (requires confirmation)
        Assert.Equal(TimeSpan.FromHours(2), tasks[1].EstimatedDuration);

        // 4. Verify email notification dispatch (NSubstitute)
        // Should only notify for Open (non-proposed) task 1
        await _mockEmailService.Received(1).SendEmailAsync(
            "admin@netrisk.io",
            "New Task Assigned: Isolate Impacted Subnet",
            "IncidentTaskTemplate",
            Arg.Any<string>(),
            Arg.Any<Dictionary<string, string>>()
        );

        // Should NOT notify for Proposed task 2
        await _mockEmailService.DidNotReceive().SendEmailAsync(
            "admin@netrisk.io",
            "New Task Assigned: Approve Backup Restore",
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Dictionary<string, string>>()
        );
    }
}
