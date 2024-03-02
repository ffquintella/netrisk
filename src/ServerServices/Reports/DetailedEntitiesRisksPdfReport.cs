using System.Globalization;
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

            //paragraph.AddFormattedText(report.Name, TextFormat.Bold);

            using var dbContext = DalService.GetContext();
            
            var entities = dbContext.Entities
                .Include(e => e.Risks).Include(e => e.EntitiesProperties)
                .Where(e => e.Risks.Count > 0)
                .ToList();

            var scores = dbContext.RiskScorings.ToList();

            foreach (var entity in entities)
            {
                paragraph = ActiveSection.AddParagraph();
                paragraph.Format.Font.Size = BodyFontSize + 2;
                
                paragraph.AddFormattedText(Localizer["Entity"] + ": ", TextFormat.Bold);
                paragraph.AddFormattedText(entity.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value );
                paragraph.AddFormattedText( " ( " + entity.DefinitionName + " )" );
                paragraph.AddLineBreak();
                paragraph.AddFormattedText( "-- " + Localizer["Risks"] + " --", TextFormat.Bold);
                paragraph.AddLineBreak();
                paragraph.AddLineBreak();
                paragraph.Format.Font.Size = BodyFontSize;
                
                foreach (var risk in entity.Risks)
                {
                    paragraph.AddFormattedText(Localizer["Subject"] + ": ", TextFormat.Bold);
                    paragraph.AddFormattedText(risk.Subject, TextFormat.NotBold);
                    paragraph.AddLineBreak();
                    
                    paragraph.AddFormattedText(Localizer["Registration Date"] + ": ", TextFormat.Bold);
                    paragraph.AddFormattedText(risk.SubmissionDate.ToString("d"), TextFormat.NotBold);
                    paragraph.AddLineBreak();
                    
                    var owner = dbContext.Users.FirstOrDefault(u => u.Value == risk.Owner);

                    if (owner != null)
                    {
                        paragraph.AddFormattedText(Localizer["Security Analyst Designated"] + ": ", TextFormat.Bold);
                        paragraph.AddFormattedText(owner.Name, TextFormat.NotBold);
                        paragraph.AddLineBreak();
                    }

                    paragraph.AddFormattedText(Localizer["Notes"] + ": ", TextFormat.Bold);
                    paragraph.AddText(risk.Notes);
                    paragraph.AddLineBreak();
                    
                    
                    paragraph.AddFormattedText("... " + Localizer["Score"] + " ...", TextFormat.Bold);
                    paragraph.AddLineBreak();
                    paragraph.AddFormattedText(Localizer["Base Likelihood"] + ": ", TextFormat.Bold);
                    paragraph.AddText(scores.FirstOrDefault(s => s.Id == risk.Id)?.ClassicLikelihood.ToString(CultureInfo.CurrentCulture) ?? string.Empty);
                    paragraph.AddLineBreak();
                    paragraph.AddFormattedText(Localizer["Base Impact"] + ": ", TextFormat.Bold);
                    paragraph.AddText(scores.FirstOrDefault(s => s.Id == risk.Id)?.ClassicImpact.ToString(CultureInfo.CurrentCulture) ?? string.Empty);
                    paragraph.AddLineBreak();
                    paragraph.AddFormattedText(Localizer["Vulnerabilities Contribution"] + ": ", TextFormat.Bold);
                    paragraph.AddText(scores.FirstOrDefault(s => s.Id == risk.Id)?.ContributingScore.ToString() ?? string.Empty);
                    paragraph.AddLineBreak();
                    
                    paragraph.AddFormattedText(Localizer["Final Score"] + ": ", TextFormat.Bold);
                    paragraph.AddText(scores.FirstOrDefault(s => s.Id == risk.Id)?.CalculatedRisk.ToString(CultureInfo.CurrentCulture) ?? string.Empty);
                    paragraph.AddLineBreak();
                    
                }
                
                paragraph.AddLineBreak();
                paragraph.AddLineBreak();
                paragraph.AddLineBreak();
                paragraph.AddLineBreak();
                
            }
            
            return Document;
        });
    }
}