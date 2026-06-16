using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace ServerServices.Services;

public class IrpMatchingRules
{
    public string? Category { get; set; }
    public int? Status { get; set; }
}

public class IrpAssigneeRule
{
    public string Type { get; set; } = "User"; // User or Role
    public string Value { get; set; } = "1"; // User ID or Role ID
}

public class IrpAutomationService(
    ILogger logger, 
    IDalService dalService, 
    ILocalizationService localization,
    IEmailService emailService)
    : LocalizableService(logger, dalService, localization), IIrpAutomationService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IEmailService _emailService = emailService;

    public async Task TriggerForIncidentAsync(Incident incident)
    {
        Logger.Information("Triggering IRP Automation Engine for Incident {IncidentId} (Category: {Category}, Status: {Status})", 
            incident.Id, incident.Category, incident.Status);

        using var dbContext = DalService.GetContext();

        try
        {
            // Resolve the incident in the current DbContext scope to prevent EF tracking/duplicate key conflicts
            var trackedIncident = await dbContext.Incidents.FindAsync(incident.Id);
            if (trackedIncident == null)
            {
                Logger.Warning("Incident {IncidentId} not found in current database context. Skipping IRP automation.", incident.Id);
                return;
            }

            var templates = await dbContext.IrpTemplates
                .Where(t => t.IsEnabled)
                .Include(t => t.Tasks)
                .ToListAsync();

            IrpTemplate? matchedTemplate = null;

            foreach (var template in templates)
            {
                if (string.IsNullOrEmpty(template.MatchingRulesJson))
                    continue;

                var rules = JsonSerializer.Deserialize<IrpMatchingRules>(template.MatchingRulesJson, JsonOptions);
                if (rules == null)
                    continue;

                bool isMatch = true;

                // Match Category
                if (!string.IsNullOrEmpty(rules.Category) && 
                    !rules.Category.Equals(trackedIncident.Category, StringComparison.OrdinalIgnoreCase))
                {
                    isMatch = false;
                }

                // Match Status
                if (rules.Status.HasValue && rules.Status.Value != trackedIncident.Status)
                {
                    isMatch = false;
                }

                if (isMatch)
                {
                    matchedTemplate = template;
                    break; // Stop at first match (SOAR priority-based matching)
                }
            }

            if (matchedTemplate == null)
            {
                Logger.Information("No active IRP template found matching Incident {IncidentId}. Skipping plan automation.", trackedIncident.Id);
                return;
            }

            Logger.Information("Incident {IncidentId} matched IRP Template '{TemplateName}' (ID: {TemplateId}). Instantiating response plan.", 
                trackedIncident.Id, matchedTemplate.Name, matchedTemplate.Id);

            await InstantiateIrpFromTemplateAsync(dbContext, matchedTemplate, trackedIncident);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error executing IRP automation trigger for Incident {IncidentId}", incident.Id);
            throw;
        }
    }

    private async Task InstantiateIrpFromTemplateAsync(DAL.Context.AuditableContext dbContext, IrpTemplate template, Incident incident)
    {
        // 1. Instantiate the Incident Response Plan
        var plan = new IncidentResponsePlan
        {
            Name = $"{template.Name} - Automated Plan",
            Description = template.Description ?? $"Automatically activated response plan based on template '{template.Name}'.",
            CreationDate = DateTime.UtcNow,
            LastUpdate = DateTime.UtcNow,
            CreatedById = incident.CreatedById > 0 ? incident.CreatedById : 1, // Fallback to Admin
            Status = 0 // Active
        };

        // Track 6 Phase 3: link the plan to the incident
        plan.ActivatedBy.Add(incident);
        dbContext.IncidentResponsePlans.Add(plan);
        await dbContext.SaveChangesAsync(); // Generate Plan ID

        var templateTaskIdMap = new Dictionary<int, int>(); // Maps IrpTemplateTask.Id -> IncidentResponsePlanTask.Id
        var generatedTasks = new List<IncidentResponsePlanTask>();

        // 2. Instantiate tasks sequentially (to resolve dependencies correctly)
        foreach (var tTask in template.Tasks.OrderBy(t => t.Id))
        {
            int assigneeId = 1; // Fallback to Admin user ID
            
            try
            {
                var assigneeRule = JsonSerializer.Deserialize<IrpAssigneeRule>(tTask.AssigneeRuleJson, JsonOptions);
                if (assigneeRule != null)
                {
                    if (assigneeRule.Type.Equals("User", StringComparison.OrdinalIgnoreCase) && int.TryParse(assigneeRule.Value, out var uId))
                    {
                        assigneeId = uId;
                    }
                    else if (assigneeRule.Type.Equals("Role", StringComparison.OrdinalIgnoreCase))
                    {
                        // Dynamic role: find the first active user belonging to this role
                        if (int.TryParse(assigneeRule.Value, out var rId))
                        {
                            var scopedUser = await dbContext.Users
                                .FirstOrDefaultAsync(u => u.RoleId == rId && u.Enabled == true);
                            if (scopedUser != null)
                            {
                                assigneeId = scopedUser.Value;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback to Admin or Incident assignee if rule parsing fails
                if (incident.AssignedToId.HasValue)
                {
                    assigneeId = incident.AssignedToId.Value;
                }
            }

            var planTask = new IncidentResponsePlanTask
            {
                PlanId = plan.Id,
                Name = tTask.Title,
                Description = tTask.InstructionsMarkdown,
                CreationDate = DateTime.UtcNow,
                LastUpdate = DateTime.UtcNow,
                CreatedById = incident.CreatedById > 0 ? incident.CreatedById : 1,
                Status = tTask.RequiresConfirmation ? 1 : 0, // 1 = Proposed, 0 = Open (SOAR Human-In-The-Loop gate)
                EstimatedDuration = TimeSpan.FromSeconds(tTask.DueOffsetSeconds),
                AssignedToId = assigneeId,
                Priority = 1 // High
            };

            dbContext.IncidentResponsePlanTasks.Add(planTask);
            await dbContext.SaveChangesAsync(); // Generates planTask ID

            templateTaskIdMap[tTask.Id] = planTask.Id;
            generatedTasks.Add(planTask);
        }

        // 3. Dispatch assignee email notifications asynchronously
        foreach (var task in generatedTasks.Where(t => t.Status == 0)) // Only notify for Open (non-proposed) tasks
        {
            var assignee = await dbContext.Users.FindAsync(task.AssignedToId);
            if (assignee != null && !string.IsNullOrEmpty(assignee.Email))
            {
                try
                {
                    Logger.Information("Dispatching notification for automated task '{TaskName}' to {Email}", task.Name, assignee.Email);
                    await _emailService.SendEmailAsync(
                        assignee.Email, 
                        $"New Task Assigned: {task.Name}", 
                        "IncidentTaskTemplate", 
                        assignee.Lang ?? "en", 
                        new Dictionary<string, string> { { "TaskName", task.Name }, { "PlanName", plan.Name } }
                    );
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "Failed to send email notification to {Email} for task {TaskId}", assignee.Email, task.Id);
                }
            }
        }
    }
}
