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

public class ReportsService(ILogger logger, DALService dalService, ILocalizationService localization)
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
        using var dbContext = DalService.GetContext();

        NrFile? fileReport = null;
        
        
        //dbContext.SaveChanges();  
        
        switch (report.Type)
        {
            case 0:
                fileReport = await CreateDetailedEntitiesRisksReportAsync(report, user);
                break;
        }

        if(fileReport != null) report.FileId = fileReport.Id;
        report.Status = (int) IntStatus.Ok;

        dbContext.Reports.Add(report);
        dbContext.SaveChanges();
        
        return report;
    }
    
    private async Task<NrFile> CreateDetailedEntitiesRisksReportAsync(Report report, User user)
    {
        var detailedEntitiesRisksPdfReport = new DetailedEntitiesRisksPdfReport(report, Localizer, DalService);
        
        var pdfData = await detailedEntitiesRisksPdfReport.GenerateReport(Localizer["Detailed Entities Risks Report"]);
        
        var file = CreateFileReport(report.Name, pdfData, user);

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
            Type = "6",
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