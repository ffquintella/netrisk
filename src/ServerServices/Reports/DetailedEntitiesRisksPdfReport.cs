using System.Globalization;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
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
                .Include(e => e.Risks)
                .ThenInclude(r => r.Mitigation).ThenInclude(m => m.PlanningStrategyNavigation)
                .Include(e => e.EntitiesProperties)
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
                    paragraph = ActiveSection.AddParagraph();
                    
                    paragraph.AddFormattedText(  "ID: ", TextFormat.Bold);
                    paragraph.AddFormattedText(risk.Id.ToString(), TextFormat.NotBold);
                    paragraph.AddLineBreak();
                    
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

                    if (risk.Mitigation != null)
                    {
                        paragraph.AddLineBreak();
                        
                        paragraph.AddFormattedText(Localizer["Mitigation"] + "", TextFormat.Italic);
                        paragraph.AddLineBreak();
                        paragraph.AddFormattedText(Localizer["Mitigation Decision"] + ": ", TextFormat.Bold);
                        paragraph.AddText(risk.Mitigation.PlanningStrategyNavigation.Name);

                        paragraph.AddLineBreak();
                        
                        paragraph.AddFormattedText(Localizer["Submission Date"] + ": ", TextFormat.Bold);
                        paragraph.AddText(risk.Mitigation.SubmissionDate.ToString("d"));
                        paragraph.AddLineBreak();
                        
                        paragraph.AddFormattedText(Localizer["Last Update"] + ": ", TextFormat.Bold);
                        paragraph.AddText(risk.Mitigation.SubmissionDate.ToString("d"));
                        paragraph.AddLineBreak();
                        
                        paragraph.AddFormattedText(Localizer["Current Solution"] + ": ", TextFormat.Bold);
                        paragraph.AddText(risk.Mitigation.CurrentSolution);
                        paragraph.AddLineBreak();
                        
                        paragraph.AddFormattedText(Localizer["Security Requirements"] + ": ", TextFormat.Bold);
                        paragraph.AddText(risk.Mitigation.SecurityRequirements);
                        paragraph.AddLineBreak();
                        
                        paragraph.AddFormattedText(Localizer["Security Recommendations"] + ": ", TextFormat.Bold);
                        paragraph.AddText(risk.Mitigation.SecurityRecommendations);
                        paragraph.AddLineBreak();
                        
                        var submissionBy = dbContext.Users.FirstOrDefault(u => u.Value == risk.Mitigation.SubmittedBy);
                        if(submissionBy != null)
                        {
                            paragraph.AddFormattedText(Localizer["Submitted By"] + ": ", TextFormat.Bold);
                            paragraph.AddText(submissionBy.Name);
                            paragraph.AddLineBreak();
                        }
                        
                        paragraph.AddFormattedText(Localizer["Planning Date"] + ": ", TextFormat.Bold);
                        paragraph.AddText(risk.Mitigation.PlanningDate.ToString("d"));
                        paragraph.AddLineBreak();
                        
                        paragraph.AddFormattedText(Localizer["Implementation %"] + ": ", TextFormat.Bold);
                        paragraph.AddText(risk.Mitigation.MitigationPercent.ToString());
                        paragraph.AddLineBreak();
                    
                        var mitigationCostStr = dbContext.MitigationCosts.FirstOrDefault(mc => risk.Mitigation != null && mc.Value == risk.Mitigation.MitigationCost);
                        var mitigationEffortStr = dbContext.MitigationEfforts.FirstOrDefault(mc => risk.Mitigation != null && mc.Value == risk.Mitigation.MitigationCost);
                    
                        paragraph.AddFormattedText(Localizer["Mitigation Cost"] + ": ", TextFormat.Bold);
                        paragraph.AddText(mitigationCostStr?.Name ?? "N/A");
                        paragraph.AddLineBreak();
                    
                        paragraph.AddFormattedText(Localizer["Mitigation Effort"] + ": ", TextFormat.Bold);
                        paragraph.AddText(mitigationEffortStr?.Name ?? "N/A");
                        paragraph.AddLineBreak(); 
                    }

                    paragraph.AddLineBreak();
                    paragraph.AddFormattedText(Localizer["Score"], TextFormat.Italic);
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
                    paragraph.AddLineBreak();
                    
                }
                
                paragraph.AddLineBreak();
                paragraph.AddLineBreak();
                
                var separator = ActiveSection.AddParagraph();
                separator.Format.Borders.Bottom.Width = 0.5;
                separator.Format.Borders.Bottom.Color = Colors.Black;
                separator.Format.SpaceAfter = "1cm";
                
                paragraph = ActiveSection.AddParagraph();
                paragraph.AddLineBreak();
                
            }
            
            return Document;
        });
    }
}