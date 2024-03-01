using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using MigraDoc.DocumentObjectModel;
using ServerServices.Services;

namespace ServerServices.Reports;

public class DetailedEntitiesRisksPdfReport(Report report, IStringLocalizer localizer, DALService dalService) : 
    TemplatedPdfReport(report, localizer, dalService)
{

    public int BodyFontSize { get; set; } = 12;
    
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

            paragraph.AddFormattedText(report.Name, TextFormat.Bold);

            using var dbContext = DalService.GetContext();
            
            var entities = dbContext.Entities
                .Include(e => e.Risks).Include(e => e.EntitiesProperties)
                .Where(e => e.Risks.Count > 0)
                .ToList();

            var scores = dbContext.RiskScorings.ToList();

            foreach (var entity in entities)
            {
                paragraph = ActiveSection.AddParagraph();
                paragraph.Format.Font.Size = BodyFontSize;
                paragraph.AddFormattedText(entity.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value, TextFormat.Bold);
                paragraph.AddLineBreak();
                paragraph.AddFormattedText(Localizer["Risks"], TextFormat.Bold);
                paragraph.AddLineBreak();
                foreach (var risk in entity.Risks)
                {
                    paragraph.AddFormattedText(risk.Subject, TextFormat.Bold);
                    paragraph.AddLineBreak();
                    paragraph.AddText(risk.Notes);
                    paragraph.AddLineBreak();
                    paragraph.AddText(Localizer["Score"] + ": " + scores.FirstOrDefault(s => s.Id == risk.Id)?.CalculatedRisk);
                    paragraph.AddLineBreak();
                }
            }
            
            return Document;
        });
    }
}