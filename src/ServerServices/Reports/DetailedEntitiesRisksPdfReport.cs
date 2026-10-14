using System.Globalization;
using System.Linq;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
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

    /// <summary>
    /// One block per entity with risks: its place in the hierarchy, the narrative of each risk (with
    /// its mitigation and score), the pre/post-treatment table, and a rule underneath.
    ///
    /// Each block keeps the font sizes the previous PDF engine actually rendered: it applied the last
    /// paragraph-level size to the whole paragraph, so the entity heading and the risk narratives
    /// came out at <see cref="BodyFontSize"/> and the default size respectively.
    /// </summary>
    protected override void ComposeBody(ColumnDescriptor body)
    {
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
            var parents = GetEntityParents(entity);

            body.Item().PaddingTop(10).Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(BodyFontSize));

                text.Span(parents).Italic();
                text.Span("\n\n");

                text.Span(Localizer["Entity"] + ": ").Bold();
                text.Span(entity.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value);
                text.Span(" ( " + entity.DefinitionName + " )");
                text.Span("\n");
                text.Span("-- " + Localizer["Risks"] + " --").Bold();
                text.Span("\n");
            });

            foreach (var risk in entity.Risks)
            {
                // REVIEW
                var lastReview = dbContext.MgmtReviews
                    .Where(mr => mr.RiskId == risk.Id)
                    .OrderByDescending(mr => mr.SubmissionDate)
                    .FirstOrDefault();

                var owner = dbContext.Users.FirstOrDefault(u => u.Value == risk.Owner);

                User? submissionBy = null;
                MitigationCost? mitigationCostStr = null;
                MitigationEffort? mitigationEffortStr = null;

                // Read before composing the text, and only for a mitigated risk: the queries dereference
                // risk.Mitigation when EF extracts their parameters.
                if (risk.Mitigation != null)
                {
                    submissionBy = dbContext.Users.FirstOrDefault(u => u.Value == risk.Mitigation.SubmittedBy);
                    mitigationCostStr = dbContext.MitigationCosts.FirstOrDefault(mc => risk.Mitigation != null && mc.Value == risk.Mitigation.MitigationCost);
                    mitigationEffortStr = dbContext.MitigationEfforts.FirstOrDefault(me => risk.Mitigation != null && me.Value == risk.Mitigation.MitigationEffort);
                }

                var score = scores.FirstOrDefault(s => s.Id == risk.Id);

                body.Item().PaddingTop(4).Text(text =>
                {
                    text.Span("ID: ").Bold();
                    text.Span(risk.Id.ToString());

                    if (lastReview != null)
                    {
                        text.Span("( " + Localizer["Last Review"] + " ");
                        text.Span(lastReview.SubmissionDate.ToString("d") + ") ");
                    }

                    text.Span("\n");

                    Line(text, Localizer["Subject"], risk.Subject);
                    Line(text, Localizer["Registration Date"], risk.SubmissionDate.ToString("d"));

                    if (owner != null)
                        Line(text, Localizer["Security Analyst Designated"], owner.Name);

                    Line(text, Localizer["Notes"], risk.Notes);

                    if (risk.Mitigation != null)
                    {
                        text.Span("\n");

                        text.Span(Localizer["Mitigation"]).Italic();
                        text.Span("\n");
                        Line(text, Localizer["Mitigation Decision"], risk.Mitigation.PlanningStrategyNavigation?.Name);
                        Line(text, Localizer["Submission Date"], risk.Mitigation.SubmissionDate.ToString("d"));
                        Line(text, Localizer["Last Update"], risk.Mitigation.SubmissionDate.ToString("d"));
                        Line(text, Localizer["Current Solution"], risk.Mitigation.CurrentSolution);
                        Line(text, Localizer["Security Requirements"], risk.Mitigation.SecurityRequirements);
                        Line(text, Localizer["Security Recommendations"], risk.Mitigation.SecurityRecommendations);

                        if (submissionBy != null)
                            Line(text, Localizer["Submitted By"], submissionBy.Name);

                        Line(text, Localizer["Planning Date"], risk.Mitigation.PlanningDate.ToString("d"));
                        Line(text, Localizer["Implementation %"], risk.Mitigation.MitigationPercent.ToString());
                        Line(text, Localizer["Mitigation Cost"], mitigationCostStr?.Name ?? "N/A");
                        Line(text, Localizer["Mitigation Effort"], mitigationEffortStr?.Name ?? "N/A");
                    }

                    text.Span("\n");
                    text.Span(Localizer["Score"]).Italic();
                    text.Span("\n");
                    Line(text, Localizer["Base Likelihood"], score?.ClassicLikelihood.ToString(CultureInfo.CurrentCulture) ?? string.Empty);
                    Line(text, Localizer["Base Impact"], score?.ClassicImpact.ToString(CultureInfo.CurrentCulture) ?? string.Empty);
                    Line(text, Localizer["Vulnerabilities Contribution"], score?.ContributingScore.ToString() ?? string.Empty);

                    var calculatedRisk = score?.CalculatedRisk ?? 0;

                    float? contributingScore = score?.ContributingScore != null
                        ? (float)score.ContributingScore.Value
                        : null;

                    var finalScore = RiskCalculationTool.CalculateTotalRiskScore(
                        calculatedRisk,
                        contributingScore);

                    Line(text, Localizer["Final Score"], finalScore.ToString(CultureInfo.CurrentCulture));
                });
            }

            AddPrePostTreatmentTable(body, entity, scores);

            body.Item().PaddingTop(12).PaddingBottom(1, Unit.Centimetre).LineHorizontal(0.5f).LineColor(Colors.Black);
        }
    }

    /// <summary>A bold "label: " and its value, ending the line.</summary>
    private static void Line(TextDescriptor text, string label, string? value)
    {
        text.Span(label + ": ").Bold();
        text.Span(value ?? string.Empty);
        text.Span("\n");
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
    private void AddPrePostTreatmentTable(ColumnDescriptor body, Entity entity, List<RiskScoring> scores)
    {
        if (entity.Risks.Count == 0) return;

        body.Item().PaddingTop(8).Text(Localizer["PrePostTreatment"].Value).Bold().FontSize(BodyFontSize);

        var headers = new[]
        {
            "ID", Localizer["Subject"].Value, Localizer["InherentRisk"].Value,
            Localizer["ResidualRisk"].Value, Localizer["Delta"].Value,
            Localizer["Implementation %"].Value
        };

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

        body.Item().DefaultTextStyle(style => style.FontSize(BodyFontSize - 3)).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                foreach (var width in new[] { 30f, 185f, 55f, 55f, 45f, 60f })
                    columns.RelativeColumn(width);
            });

            table.Header(header =>
            {
                foreach (var title in headers)
                    Cell(header.Cell()).Text(title).Bold();
            });

            foreach (var row in rows)
            {
                var score = row.Score!;

                Cell(table.Cell()).Text(row.Risk.Id.ToString(CultureInfo.CurrentCulture));
                Cell(table.Cell()).Text(row.Risk.Subject ?? string.Empty);
                Cell(table.Cell()).Text(score.CalculatedRisk.ToString("0.00", CultureInfo.CurrentCulture));

                Cell(table.Cell()).Text(score.ResidualRisk?.ToString("0.00", CultureInfo.CurrentCulture) ?? "—");

                Cell(table.Cell()).Text(score.ResidualRisk == null
                    ? "—"
                    : (score.ResidualRisk.Value - score.CalculatedRisk).ToString("+0.00;-0.00;0.00",
                        CultureInfo.CurrentCulture));

                Cell(table.Cell()).Text(row.Risk.Mitigation == null
                    ? "—"
                    : row.Risk.Mitigation.MitigationPercent.ToString(CultureInfo.CurrentCulture) + "%");
            }
        });
    }

    private static IContainer Cell(IContainer cell) => cell.Border(0.25f).PaddingHorizontal(3).PaddingVertical(1);
}
