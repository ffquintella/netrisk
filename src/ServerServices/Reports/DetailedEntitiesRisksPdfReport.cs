using System.Globalization;
using System.Linq;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.DocumentObjectModel.Tables;
using ServerServices.Services;
using Tools.Risks;

namespace ServerServices.Reports;

public class DetailedEntitiesRisksPdfReport(Report report, IStringLocalizer localizer, IDalService dalService) : 
    TemplatedPdfReport(report, localizer, dalService)
{

    
    public string GetEntityParents(Entity entity)
    {
        var parents = "";
        if (entity.Parent != null)
        {
            using var dbContext = DalService.GetContext();
            var parent = dbContext.Entities.Include(e => e.EntitiesProperties).FirstOrDefault(e => e.Id == entity.Parent);
            if (parent != null)
            {
                parents += GetEntityParents(parent);
                parents += " > ";
                parents += parent.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value;
            }
        }

        return parents;
    }
    
    public int BodyFontSize { get; set; } = 12;

    protected override async Task<Document> AddBody()
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
                .ThenInclude(r => r.Mitigation).ThenInclude(m => m!.PlanningStrategyNavigation)
                .Include(e => e.EntitiesProperties)
                .Where(e => e.Risks.Count > 0)
                .ToList();

            var scores = dbContext.RiskScorings.ToList();

            foreach (var entity in entities)
            {
                paragraph = ActiveSection.AddParagraph();
                paragraph.Format.Font.Size = BodyFontSize - 2;

                var parents = GetEntityParents(entity);
                
                paragraph.AddFormattedText(parents , TextFormat.Italic);
                paragraph.AddLineBreak();
                paragraph.AddLineBreak();
                
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
                    
                    // REVIEW 
                    var lastReview = dbContext.MgmtReviews
                        .Where(mr => mr.RiskId == risk.Id)
                        .OrderBy(mr => mr.SubmissionDate)
                        .FirstOrDefault();
                    
                    if(lastReview != null)
                    {
                        paragraph.AddFormattedText( "( " + Localizer["Last Review"] + " ", TextFormat.NotBold);
                        paragraph.AddFormattedText(lastReview.SubmissionDate.ToString("d") + ") ", TextFormat.NotBold);
                    }
                    
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

                    var calculatedRisk = scores.FirstOrDefault(s => s.Id == risk.Id)?.CalculatedRisk;
                    if (calculatedRisk == null) calculatedRisk = 0;
                    
                    float? contributingScore;
                    if (scores.FirstOrDefault(s => s.Id == risk.Id)?.ContributingScore != null)
                        contributingScore =
                            (float?)scores.FirstOrDefault(s => s.Id == risk.Id)?.ContributingScore!.Value ?? 0;
                    else contributingScore = null;
                    
                    var finalScore = RiskCalculationTool.CalculateTotalRiskScore(
                        calculatedRisk.Value,
                        contributingScore);
                    
                    paragraph.AddText(finalScore.ToString(CultureInfo.CurrentCulture) ?? string.Empty);
                    paragraph.AddLineBreak();
                    paragraph.AddLineBreak();
                    
                }
                
                AddPrePostTreatmentTable(entity, scores);

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

    /// <summary>
    /// The pre/post-treatment table for one entity (Track 8 milestone 8.2.3).
    ///
    /// The narrative above states each risk's score in prose, which is unreadable as a comparison —
    /// the question a reviewer actually asks is "which of these did the treatment move, and by how
    /// much", and that is a table or it is nothing. Rows are ordered by the size of the reduction so
    /// the treatments that bought the least sit at the top, which is where attention belongs.
    ///
    /// A risk with no residual score appears with a dash rather than being dropped. An untreated risk
    /// missing from a pre/post table reads as a register where everything has been treated.
    /// </summary>
    private void AddPrePostTreatmentTable(Entity entity, List<RiskScoring> scores)
    {
        if (ActiveSection == null) return;
        if (entity.Risks.Count == 0) return;

        var heading = ActiveSection.AddParagraph();
        heading.Format.SpaceBefore = 8;
        heading.Format.Font.Size = BodyFontSize;
        heading.AddFormattedText(Localizer["PrePostTreatment"], TextFormat.Bold);

        var table = ActiveSection.AddTable();
        table.Borders.Width = 0.25;
        table.Format.Font.Size = BodyFontSize - 3;

        foreach (var width in new[] { 30.0, 185.0, 55.0, 55.0, 45.0, 60.0 })
            table.AddColumn(Unit.FromPoint(width));

        var header = table.AddRow();
        header.HeadingFormat = true;

        var headers = new[]
        {
            "ID", Localizer["Subject"].Value, Localizer["InherentRisk"].Value,
            Localizer["ResidualRisk"].Value, Localizer["Delta"].Value,
            Localizer["Implementation %"].Value
        };

        for (var i = 0; i < headers.Length; i++)
            header.Cells[i].AddParagraph().AddFormattedText(headers[i], TextFormat.Bold);

        var rows = entity.Risks
            .Select(risk => new
            {
                Risk = risk,
                Score = scores.FirstOrDefault(s => s.Id == risk.Id)
            })
            .Where(r => r.Score != null)
            // Smallest reduction first. A risk with no residual sorts with the untreated ones rather
            // than at either extreme, because "not computed" is not "not reduced".
            .OrderBy(r => r.Score!.ResidualRisk == null
                ? 0
                : r.Score.CalculatedRisk - r.Score.ResidualRisk.Value)
            .ThenBy(r => r.Risk.Id)
            .ToList();

        foreach (var row in rows)
        {
            var cells = table.AddRow();
            var score = row.Score!;

            cells.Cells[0].AddParagraph(row.Risk.Id.ToString(CultureInfo.CurrentCulture));
            cells.Cells[1].AddParagraph(row.Risk.Subject ?? string.Empty);
            cells.Cells[2].AddParagraph(score.CalculatedRisk.ToString("0.00", CultureInfo.CurrentCulture));

            cells.Cells[3].AddParagraph(score.ResidualRisk?.ToString("0.00", CultureInfo.CurrentCulture) ?? "—");

            cells.Cells[4].AddParagraph(score.ResidualRisk == null
                ? "—"
                : (score.ResidualRisk.Value - score.CalculatedRisk).ToString("+0.00;-0.00;0.00",
                    CultureInfo.CurrentCulture));

            cells.Cells[5].AddParagraph(row.Risk.Mitigation == null
                ? "—"
                : row.Risk.Mitigation.MitigationPercent.ToString(CultureInfo.CurrentCulture) + "%");
        }
    }
}
