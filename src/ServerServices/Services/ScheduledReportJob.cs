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

namespace ServerServices.Services;

public class ScheduledReportJob
{
    private readonly IDalService _dalService;
    private readonly IQuestPdfRenderingService _questPdfRenderingService;
    private readonly IFluentEmail _fluentEmail;

    public ScheduledReportJob(
        IDalService dalService, 
        IQuestPdfRenderingService questPdfRenderingService, 
        IFluentEmail fluentEmail)
    {
        _dalService = dalService;
        _questPdfRenderingService = questPdfRenderingService;
        _fluentEmail = fluentEmail;
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

            // Fetch high-priority entities to populate the customizable report sections (e.g. Incidents)
            var incidents = await context.Incidents
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
