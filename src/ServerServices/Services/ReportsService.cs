using System.Text.Json;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Model;
using Model.Exceptions;
using Model.File;
using Model.Reports;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Reports;
using Tools;
using Tools.Security;
using ILogger = Serilog.ILogger;


namespace ServerServices.Services;

public class ReportsService(
    ILogger logger,
    IDalService dalService,
    ILocalizationService localization,
    IQuestPdfRenderingService questPdfRenderingService,
    IAuditTrailService auditTrail)
    : LocalizableService(logger, dalService, localization), IReportsService
{
    public List<Report> GetAll()
    {
        using var dbContext = DalService.GetContext();
        
        var reports = dbContext.Reports.ToList();

        return reports;
    }

    public async Task<Report> CreateAsync(Report report, User user)
    {
        await using var dbContext = DalService.GetContext();

        NrFile? fileReport = null;
        
        //dbContext.SaveChanges();  
        report.Status = (int) IntStatus.AwaitingInternalResponse;
        
        switch (report.Type)
        {
            case 0:
                fileReport = await CreateDetailedEntitiesRisksReportAsync(report, user);
                report.Status = (int) IntStatus.Ok;
                break;
            case 1:
                fileReport = await CreateHostVulnerabilitiesPrioritizationAsync(report, user);
                break;
            case ReportParameters.TemplateReportType:
                fileReport = await CreateTemplateReportAsync(report, user);
                report.Status = (int) IntStatus.Ok;
                break;
            case ReportParameters.GovernanceEvidenceReportType:
                fileReport = await CreateGovernanceEvidenceReportAsync(report, user);
                report.Status = (int) IntStatus.Ok;
                break;
        }

        if (fileReport == null)
        {
            Logger.Error("Error creating report");
            throw new DataNotFoundException("report", report.Type.ToString());
        }
        
        report.FileId = fileReport.Id;

        dbContext.Reports.Add(report);
        dbContext.SaveChanges();
        
        return report;
    }
    
    private async Task<NrFile> CreateDetailedEntitiesRisksReportAsync(Report report, User user)
    {
        var detailedEntitiesRisksPdfReport = new DetailedEntitiesRisksPdfReport(report, Localizer, DalService);
        
        var pdfData = await detailedEntitiesRisksPdfReport.GenerateReportAsync(Localizer["Detailed Entities Risks Report"]);
        
        var file = CreateFileReport(report.Name, pdfData, user);

        return file;

    }

    /// <summary>
    /// The auditor evidence pack (Track 8 milestone 8.4.2, campaign evidence per 8.6.5).
    ///
    /// It runs through this engine rather than as a standalone download so it lands in the reports
    /// list as a stored <c>NrFile</c> like every other report — which is what makes a quarterly
    /// evidence pack schedulable, and what makes "we produced this on the 3rd" a record rather than a
    /// claim.
    /// </summary>
    private async Task<NrFile> CreateGovernanceEvidenceReportAsync(Report report, User user)
    {
        var parameters = string.IsNullOrWhiteSpace(report.Parameters)
            ? null
            : JsonSerializer.Deserialize<ReportParameters>(report.Parameters);

        // A year back is the default window because that is the look-back an annual audit asks for,
        // and an evidence pack with no period at all would silently mean "everything we still have",
        // which the retention policy makes an unstable answer.
        var toUtc = parameters?.PeriodEnd ?? DateTime.UtcNow;
        var fromUtc = parameters?.PeriodStart ?? toUtc.AddYears(-1);

        if (toUtc < fromUtc)
            throw new DataProcessingException("ReportsService", "CreateGovernanceEvidenceReportAsync",
                "The evidence period ends before it starts");

        var pack = await auditTrail.GetEvidencePackAsync(parameters?.EntityId, fromUtc, toUtc,
            $"{user.Name} ({user.Login}, #{user.Value})");

        var pdfReport = new GovernanceEvidencePdfReport(report, Localizer, DalService, pack);

        var pdfData = await pdfReport.GenerateReportAsync(Localizer["GovernanceEvidencePack"]);

        return CreateFileReport(report.Name, pdfData, user);
    }

    private async Task<NrFile> CreateTemplateReportAsync(Report report, User user)
    {
        var parameters = string.IsNullOrWhiteSpace(report.Parameters)
            ? null
            : JsonSerializer.Deserialize<ReportParameters>(report.Parameters);

        if (parameters?.TemplateId == null)
        {
            Logger.Error("Template report created without a template id");
            throw new DataProcessingException("ReportsService", "CreateTemplateReportAsync",
                "Report template id not provided");
        }

        await using var dbContext = DalService.GetContext();

        // Use the latest version of the selected template.
        var version = await dbContext.ReportTemplateVersions
            .AsNoTracking()
            .Where(v => v.TemplateId == parameters.TemplateId.Value)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync();

        if (version == null)
            throw new DataNotFoundException("ReportTemplate", parameters.TemplateId.Value.ToString());

        // Populate the customizable report sections with the same data source used by
        // scheduled template exports (see ScheduledReportJob).
        var incidents = await dbContext.Incidents
            .AsNoTracking()
            .OrderByDescending(i => i.Id)
            .Take(50)
            .ToListAsync();

        var pdfData = await questPdfRenderingService.RenderFromTemplateAsync(
            version.LayoutJson,
            version.BrandingJson,
            incidents,
            report.Name);

        return CreateFileReport(report.Name, pdfData, user);
    }

    private async Task<NrFile> CreateHostVulnerabilitiesPrioritizationAsync(Report report, User user)
    {
        try
        {
            var file = await CreateEmptyReportFile(report.Name, user);

            _ = UpdateHostVulnerabilitiesPriorizationAsync(report, file);
        
            return file;  
        }catch (Exception e)
        {
            Logger.Error(e, "Error creating Host Vulnerabilities Prioritization Report");
            throw new DataProcessingException("ReportsService", "CreateHostVulnerabilitiesPrioritizationAsync", "Error creating Host Vulnerabilities Prioritization Report", e);
        }

    }

    private async Task UpdateHostVulnerabilitiesPriorizationAsync(Report report ,NrFile file)
    {
        try
        {
            var hostVulnerabilitiesPrioritizationReport = new HostVulnerabilitiesPrioritizationReport(report, Localizer, DalService);
            var pdfData = await hostVulnerabilitiesPrioritizationReport.GenerateReportAsync(Localizer["Host Vulnerabilities Prioritization Report"]);
            _ = UpdateFileContent(file, pdfData);
        
            await using var dbContext = DalService.GetContext();
        
            dbContext.Reports.Update(report);
        
            report.Status = (int) IntStatus.Ok;
            await dbContext.SaveChangesAsync();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }

    }

    private async Task<NrFile> CreateEmptyReportFile(string fileName, User user)
    {
        // Same reasoning as FilesService.Create (Track 7 finding NR-2026-017): the unique name is the
        // capability, because GET /Files/{name} has no per-file ownership check. It must be
        // unguessable rather than derived from a file name the requester already knows.
        var hash = HashTool.CreateSha256(RandomGenerator.RandomToken(32));
        
        var file = new NrFile
        {
            Id = 0,
            Name = fileName,
            Type = "19", // PDF
            Content = [],
            ViewType = (int)FileViewType.Report,
            Size = 0,
            Timestamp = DateTime.Now,
            UniqueName = hash,
            User = user.Value
        };
        
        await using var dbContext = DalService.GetContext();
        
        dbContext.NrFiles.Add(file);
        await dbContext.SaveChangesAsync();
        
        return file;
    }

    private async Task<NrFile> UpdateFileContent(NrFile file, byte[] data)
    {
        await using var dbContext = DalService.GetContext();
        
        //dbContext.NrFiles.Update(file);
        
        var dbFile = await dbContext.NrFiles.FindAsync(file.Id);

        if (dbFile == null)
        {
            Log.Error("Error saving file: {FileId}", file.Id);
            throw new DataProcessingException("ReportsService", "UpdateFileContent", "File not found");
        }
        
        dbFile.Content = data;
        dbFile.Size = data.Length;
        dbFile.Timestamp = DateTime.Now;

        try
        {
            await dbContext.SaveChangesAsync();

            return file;
        }catch (Exception e)
        {
            Log.Error(e, "Error saving file: {FileId}", file.Id);
            throw new DataProcessingException("ReportsService", "UpdateFileContent", "Error saving file", e);
        }
        
    }
    

    private NrFile CreateFileReport(string fileName, byte[] data, User user)
    {
        // Same reasoning as FilesService.Create (Track 7 finding NR-2026-017): the unique name is the
        // capability, because GET /Files/{name} has no per-file ownership check. It must be
        // unguessable rather than derived from a file name the requester already knows.
        var hash = HashTool.CreateSha256(RandomGenerator.RandomToken(32));
        
        var file = new NrFile
        {
            Id = 0,
            Name = fileName,
            Type = "19", // PDF
            Content = data,
            ViewType = (int)FileViewType.Report,
            Size = data.Length,
            Timestamp = DateTime.Now,
            UniqueName = hash,
            User = user.Value
        };
        
        using var dbContext = DalService.GetContext();
        
        dbContext.NrFiles.Add(file);
        dbContext.SaveChanges();
        
        return file;
    }
    
    public void Delete(int reportId)
    {
        using var dbContext = DalService.GetContext();

        var report = dbContext.Reports.Find(reportId);
        
        if (report == null)
        {
            throw new DataNotFoundException("report",reportId.ToString());
        }
        
        dbContext.Reports.Remove(report);
        dbContext.SaveChanges();
    }
}