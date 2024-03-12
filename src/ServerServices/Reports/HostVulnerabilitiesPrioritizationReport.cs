using DAL.Entities;
using Microsoft.Extensions.Localization;
using MigraDoc.DocumentObjectModel;
using ServerServices.Services;

namespace ServerServices.Reports;

public class HostVulnerabilitiesPrioritizationReport(Report report, IStringLocalizer localizer, DALService dalService): TemplatedPdfReport(report, localizer, dalService)
{
    
    public override async Task<Document> AddBody()
    {
        if(Document == null)
            throw new Exception("Document is null");

        return await Task.Run(() =>
        {
            if (ActiveSection == null)
                throw new Exception("ActiveSection is null");

            var paragraph = ActiveSection.AddParagraph();

            paragraph.Format.Font.Size = TitleFontSize;
            
            using var dbContext = DalService.GetContext();
            
            var vulnerabilities = dbContext.Vulnerabilities.ToList();
            
            return Document;
        });
        
        
    }
}