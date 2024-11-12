using DAL.Entities;
using Microsoft.Extensions.Localization;
using Model;
using Model.Exceptions;
using Model.File;
using ServerServices.Interfaces;
using ServerServices.Reports;
using Tools;
using Tools.Security;
using ILogger = Serilog.ILogger;


namespace ServerServices.Services;

public class ReportsService(ILogger logger, IDalService dalService, ILocalizationService localization)
    : LocalizableService(logger, dalService, localization), IReportsService
{
    public List<Report> GetAll()
    {
        using var dbContext = DalService.GetContext();
        
        var reports = dbContext.Reports.ToList();

        return reports;
    }

    public async Task<Report> Create(Report report, User user)
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

    private async Task<NrFile> CreateHostVulnerabilitiesPrioritizationAsync(Report report, User user)
    {
        
        var file = await CreateEmptyReportFile(report.Name, user);

        _ = Task.Run(() => UpdateHostVulnerabilitiesPriorizationAsync(report, file));
        
        //_ = UpdateHostVulnerabilitiesPriorizationAsync(report, file);
        
        //var file = CreateFileReport(report.Name, pdfData, user);

        return file; 
    }

    private async Task UpdateHostVulnerabilitiesPriorizationAsync(Report report ,NrFile file)
    {
        var hostVulnerabilitiesPrioritizationReport = new HostVulnerabilitiesPrioritizationReport(report, Localizer, DalService);
        var pdfData = await hostVulnerabilitiesPrioritizationReport.GenerateReportAsync(Localizer["Host Vulnerabilities Prioritization Report"]);
        _ = UpdateFileContent(file, pdfData);
        
        await using var dbContext = DalService.GetContext();
        
        dbContext.Reports.Update(report);
        
        report.Status = (int) IntStatus.Ok;
        
        
        await dbContext.SaveChangesAsync();
    }

    private async Task<NrFile> CreateEmptyReportFile(string fileName, User user)
    {
        var key = RandomGenerator.RandomString(15);
        var hash = HashTool.CreateSha1(fileName + key);
        
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
        
        file.Content = data;
        file.Size = data.Length;
        file.Timestamp = DateTime.Now;
        
        
        dbContext.NrFiles.Update(file);
        await dbContext.SaveChangesAsync();

        return file;

    }
    

    private NrFile CreateFileReport(string fileName, byte[] data, User user)
    {
        var key = RandomGenerator.RandomString(15);
        var hash = HashTool.CreateSha1(fileName + key);
        
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