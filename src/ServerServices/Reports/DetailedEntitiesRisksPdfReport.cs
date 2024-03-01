using DAL.Entities;
using Microsoft.Extensions.Localization;
using MigraDoc.DocumentObjectModel;

namespace ServerServices.Reports;

public class DetailedEntitiesRisksPdfReport(Report report, IStringLocalizer localizer) : TemplatedPdfReport(report, localizer)
{

    public override async Task<Document> AddBody()
    {
        if(Document == null)
            throw new Exception("Document is null");
        
        return await Task.Run(() =>
        {
            if(ActiveSection == null)
                throw new Exception("ActiveSection is null");
            
            var paragraph = ActiveSection.AddParagraph();

            paragraph.Format.Font.Size = TitleFontSize;

            paragraph.AddFormattedText("TESTE REPORT", TextFormat.Bold);

            return Document;
        });
    }
}