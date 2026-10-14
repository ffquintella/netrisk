using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DAL.Entities;
using FluentEmail.Core;
using Microsoft.EntityFrameworkCore;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Security;

namespace ServerServices.Services;

/// <summary>
/// Renders a report schedule and mails it to the recipients its creator chose.
///
/// The job host's principal is unrestricted (T237, S39 D-01), and this job runs what a user configured — recipients
/// included — so its reads must not be made under that grant: they would hand any user who can save a schedule every
/// entity's incidents by email. The schedule itself is read under the host's principal; every row of the report is read
/// through <see cref="IDalService.GetContextFor"/> under the creator's entity scope as
/// <see cref="IUserEntityScopeResolver"/> resolves it at run time — hierarchical (T292), and nothing at all when the creator
/// is gone, disabled, locked out or no longer holds <see cref="RequiredPermission"/> (T237 review).
/// <c>ScheduledReportScopeTest</c> pins it.
/// </summary>
public class ScheduledReportJob
{
    /// <summary>
    /// The permission that gates the report-schedule endpoints, and that the creator must still hold when the job runs:
    /// the existing <c>reports</c> ("Able to access reports", Data/1.sql).
    /// </summary>
    public const string RequiredPermission = "reports";

    /// <summary>The status a run that read nothing because its creator may not run it any more records.</summary>
    public const string SkippedNoOwnerStatus = "Skipped: the schedule's creator may not run it (no creator, disabled, locked out or without the reports permission)";

    private readonly IDalService _dalService;
    private readonly IQuestPdfRenderingService _questPdfRenderingService;
    private readonly IFluentEmail _fluentEmail;
    private readonly IUserEntityScopeResolver _scopeResolver;

    public ScheduledReportJob(
        IDalService dalService, 
        IQuestPdfRenderingService questPdfRenderingService, 
        IFluentEmail fluentEmail,
        IUserEntityScopeResolver scopeResolver)
    {
        _dalService = dalService;
        _questPdfRenderingService = questPdfRenderingService;
        _fluentEmail = fluentEmail;
        _scopeResolver = scopeResolver;
    }

    public async Task Run(int scheduleId)
    {
        Log.Information("Starting ScheduledReportJob for schedule {ScheduleId}", scheduleId);

        using var context = _dalService.GetContext();
        var schedule = await context.ReportSchedules
            .Include(s => s.ReportTemplateVersion)
                .ThenInclude(v => v.Template)
            .FirstOrDefaultAsync(s => s.Id == scheduleId);

        if (schedule == null)
        {
            Log.Error("Report schedule with ID {ScheduleId} not found", scheduleId);
            return;
        }

        if (!schedule.IsEnabled)
        {
            Log.Warning("Report schedule with ID {ScheduleId} is disabled. Skipping execution.", scheduleId);
            return;
        }

        try
        {
            schedule.LastRunAt = DateTime.UtcNow;

            // The creator's scope as it stands now — never the job host's (T237 review). No creator, or one who may not
            // act any more, renders and sends nothing.
            var scope = schedule.CreatedById is { } creatorId
                ? await _scopeResolver.ResolveAsync(creatorId, RequiredPermission)
                : null;

            if (scope is null)
            {
                schedule.LastStatus = SkippedNoOwnerStatus;
                Log.Warning("Report schedule {ScheduleId} skipped: its creator {CreatorId} may not run it",
                    scheduleId, schedule.CreatedById);
                return;
            }

            await using var data = _dalService.GetContextFor(scope);

            // Fetch high-priority entities to populate the customizable report sections (e.g. Incidents)
            var incidents = await data.Incidents
                .AsNoTracking()
                .OrderByDescending(i => i.Id)
                .Take(50)
                .ToListAsync();

            // Render PDF using the dynamic QuestPDF engine
            var pdfData = await _questPdfRenderingService.RenderFromTemplateAsync(
                schedule.ReportTemplateVersion.LayoutJson,
                schedule.ReportTemplateVersion.BrandingJson,
                incidents,
                $"{schedule.ReportTemplateVersion.Template.Name} - Automated Export"
            );

            // Deserialize list of recipients
            var recipients = JsonSerializer.Deserialize<List<string>>(schedule.RecipientsJson) ?? new List<string>();

            // Send dynamic attachment-bearing email via FluentEmail
            foreach (var recipient in recipients)
            {
                Log.Information("Sending scheduled report {TemplateName} to {Recipient}", 
                    schedule.ReportTemplateVersion.Template.Name, recipient);

                using var pdfStream = new MemoryStream(pdfData);

                await _fluentEmail
                    .To(recipient)
                    .Subject($"{schedule.ReportTemplateVersion.Template.Name} - Automated Export")
                    .Body($"Please find attached the automated GRC report export for {schedule.ReportTemplateVersion.Template.Name}, generated on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC.")
                    .Attach(new FluentEmail.Core.Models.Attachment
                    {
                        Data = pdfStream,
                        Filename = $"{schedule.ReportTemplateVersion.Template.Name.Replace(" ", "_")}_{DateTime.UtcNow:yyyyMMdd}.pdf",
                        ContentType = "application/pdf"
                    })
                    .SendAsync();
            }

            schedule.LastStatus = "Success";
            Log.Information("Successfully completed ScheduledReportJob for schedule {ScheduleId}", scheduleId);
        }
        catch (Exception ex)
        {
            schedule.LastStatus = $"Failed: {ex.Message}";
            Log.Error(ex, "Error executing ScheduledReportJob for schedule {ScheduleId}", scheduleId);
        }
        finally
        {
            context.ReportSchedules.Update(schedule);
            await context.SaveChangesAsync();
        }
    }
}
