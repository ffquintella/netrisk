using System.Globalization;
using Microsoft.Extensions.Localization;
using Model.Governance;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using ServerServices.Services;
using Report = DAL.Entities.Report;

namespace ServerServices.Reports;

/// <summary>
/// The auditor evidence pack rendered through the 2.1 reporting engine (Track 8 milestone 8.4.2,
/// with the campaign evidence of 8.6.5 folded in).
///
/// It goes through <see cref="TemplatedPdfReport"/> rather than being written ad hoc so it inherits
/// what the engine already guarantees — the branded header, the page numbering, the font embedding
/// that makes the file render the same on the auditor's machine as on ours — and so it appears in
/// the reports list, gets stored as an <c>NrFile</c>, and can be scheduled like any other report
/// instead of being a download somebody has to remember to take.
///
/// The pack is passed in already assembled. This class decides only how it reads; what counts as
/// evidence is <c>AuditTrailService.GetEvidencePackAsync</c>'s decision, so the CSV and the PDF
/// cannot disagree.
/// </summary>
public class GovernanceEvidencePdfReport(
    Report report,
    IStringLocalizer localizer,
    IDalService dalService,
    GovernanceEvidencePack pack)
    : TemplatedPdfReport(report, localizer, dalService)
{
    public int BodyFontSize { get; set; } = 9;

    private static readonly CultureInfo Iso = CultureInfo.InvariantCulture;

    protected override void ComposeBody(ColumnDescriptor body)
    {
        AddScope(body);

        AddAcceptances(body);
        AddReviews(body);
        AddCampaignDecisions(body);
        AddChanges(body);
    }

    /// <summary>
    /// What the pack covers, who asked for it and when. An evidence file that does not state its own
    /// period cannot be filed against a finding — and the reader has no way to tell an empty section
    /// from a section that was never queried.
    /// </summary>
    private void AddScope(ColumnDescriptor body)
    {
        body.Item().PaddingTop(10).DefaultTextStyle(style => style.FontSize(BodyFontSize + 1)).Column(lines =>
        {
            Line(lines, Localizer["Entity"], pack.EntityName);
            Line(lines, Localizer["Period"],
                $"{pack.FromUtc.ToString("yyyy-MM-dd", Iso)} → {pack.ToUtc.ToString("yyyy-MM-dd", Iso)} (UTC)");
            Line(lines, Localizer["GeneratedAt"], pack.GeneratedAtUtc.ToString("u", Iso));
            Line(lines, Localizer["RequestedBy"], pack.RequestedBy);
            Line(lines, Localizer["Contents"],
                $"{pack.Acceptances.Count} {Localizer["RiskAcceptances"]}, " +
                $"{pack.Reviews.Count} {Localizer["MgmtReviews"]}, " +
                $"{pack.CampaignDecisions.Count} {Localizer["BusinessReviewDecisions"]}, " +
                $"{pack.Changes.Count} {Localizer["FieldChanges"]}");
        });

        if (pack.ChangesTruncated)
            body.Item().Text(Localizer["EvidenceTruncatedMSG"].Value).Bold().FontSize(BodyFontSize);
    }

    private void AddAcceptances(ColumnDescriptor body)
    {
        Heading(body, Localizer["RiskAcceptances"]);

        if (Empty(body, pack.Acceptances.Count)) return;

        foreach (var acceptance in pack.Acceptances)
        {
            body.Item().PaddingTop(6).DefaultTextStyle(style => style.FontSize(BodyFontSize)).Column(lines =>
            {
                lines.Item().Text($"#{acceptance.Id} {acceptance.Name}").Bold();

                Line(lines, Localizer["Risk"],
                    acceptance.RiskId == null ? "-" : $"#{acceptance.RiskId} {acceptance.RiskSubject}");
                // StatusLabel, not Status: the resx already had a lowercase `status` key for the
                // Sieve filter vocabulary, and resx names are case-insensitive, so this entry was
                // being dropped at build time (MSB3568) and its pt-BR translation never reached a
                // reader.
                Line(lines, Localizer["StatusLabel"], acceptance.Status);
                Line(lines, Localizer["AuthorizingManager"], acceptance.AuthorizingManager);

                if (acceptance.RequestedBy != null)
                    Line(lines, Localizer["RequestedBy"], acceptance.RequestedBy);

                Line(lines, Localizer["Period"],
                    $"{acceptance.StartDate.ToString("yyyy-MM-dd", Iso)} → " +
                    $"{acceptance.ExpiresAt.ToString("yyyy-MM-dd", Iso)}");

                if (acceptance.ResidualScoreSnapshot != null)
                    Line(lines, Localizer["ResidualRiskAtAcceptance"],
                        acceptance.ResidualScoreSnapshot.Value.ToString("0.00", Iso));

                if (acceptance.FromCampaign)
                    Line(lines, Localizer["Origin"], Localizer["BusinessReviewCampaign"]);

                if (!string.IsNullOrWhiteSpace(acceptance.BusinessJustification))
                    Line(lines, Localizer["BusinessJustification"], acceptance.BusinessJustification!);

                if (!string.IsNullOrWhiteSpace(acceptance.CompensatingControls))
                    Line(lines, Localizer["CompensatingControls"], acceptance.CompensatingControls!);

                if (acceptance.RevokedAt != null)
                    Line(lines, Localizer["Revoked"],
                        $"{acceptance.RevokedAt.Value.ToString("yyyy-MM-dd", Iso)} — " +
                        $"{acceptance.RevokedBy} — {acceptance.RevocationReason}");
            });
        }
    }

    private void AddReviews(ColumnDescriptor body)
    {
        Heading(body, Localizer["MgmtReviews"]);

        if (Empty(body, pack.Reviews.Count)) return;

        var rows = new List<IReadOnlyList<string>>();

        foreach (var review in pack.Reviews)
        {
            var countersignature = review.RequiresCountersignature
                ? review.SecondReviewer == null
                    ? Localizer["PendingCountersignature"].Value
                    : $"{review.SecondReviewer} — {review.SecondReviewAt?.ToString("yyyy-MM-dd", Iso)}"
                : "-";

            // The override reason is carried into the countersignature column rather than dropped:
            // a review where somebody deliberately broke segregation of duties is the row an auditor
            // is looking for, and burying it would defeat the point of recording it.
            if (!string.IsNullOrWhiteSpace(review.SegregationOverrideReason))
                countersignature += $" [{Localizer["SegregationOverride"]}: {review.SegregationOverrideReason}]";

            rows.Add([
                review.SubmissionDate.ToString("yyyy-MM-dd", Iso),
                $"#{review.RiskId}",
                review.Reviewer,
                countersignature,
                review.Comments
            ]);
        }

        AddTable(body, [70, 55, 110, 110, 145],
            [Localizer["Date"], Localizer["Risk"], Localizer["Reviewer"],
                Localizer["Countersignature"], Localizer["Comments"]],
            rows);
    }

    private void AddCampaignDecisions(ColumnDescriptor body)
    {
        Heading(body, Localizer["BusinessReviewDecisions"]);

        if (Empty(body, pack.CampaignDecisions.Count)) return;

        var rows = new List<IReadOnlyList<string>>();

        foreach (var decision in pack.CampaignDecisions)
        {
            var notes = decision.DecisionNotes ?? "";

            if (decision.EscalatedTo != null)
                notes = $"{Localizer["EscalatedTo"]}: {decision.EscalatedTo}. {notes}".Trim();

            if (decision.RiskAcceptanceId != null)
                notes = $"{Localizer["RiskAcceptance"]} #{decision.RiskAcceptanceId}. {notes}".Trim();

            rows.Add([
                $"{decision.CampaignName} ({decision.CampaignStatus})",
                $"#{decision.RiskId}",
                decision.Rank?.ToString(Iso) ?? "-",
                decision.Decision,
                decision.DecidedBy ?? "-",
                notes
            ]);
        }

        AddTable(body, [100, 45, 30, 70, 90, 155],
            [Localizer["Campaign"], Localizer["Risk"], Localizer["BusinessRank"],
                Localizer["Decision"], Localizer["DecidedBy"], Localizer["Notes"]],
            rows);
    }

    private void AddChanges(ColumnDescriptor body)
    {
        Heading(body, Localizer["FieldChanges"]);

        if (Empty(body, pack.Changes.Count)) return;

        var rows = pack.Changes
            .Select(IReadOnlyList<string> (change) => [
                change.OccurredAt.ToString("yyyy-MM-dd HH:mm", Iso),
                $"{change.EntityType} #{change.EntityId}",
                change.Field ?? "-",
                change.Action,
                Truncate(change.OldValue),
                Truncate(change.NewValue),
                change.Actor
            ])
            .ToList();

        AddTable(body, [80, 75, 70, 45, 75, 80, 65],
            [Localizer["When"], Localizer["Record"], Localizer["Field"],
                Localizer["Action"], Localizer["From"], Localizer["To"], Localizer["Actor"]],
            rows);
    }

    /// <summary>
    /// Long free text is clipped in the table so one 4 000-character justification cannot push the
    /// rest of the trail off the page. The full value is in the CSV export, which is where somebody
    /// reading a specific change goes.
    /// </summary>
    private static string Truncate(string? value, int max = 60)
    {
        if (string.IsNullOrEmpty(value)) return "-";
        return value.Length <= max ? value : value[..max] + "…";
    }

    private void Heading(ColumnDescriptor body, string title)
    {
        body.Item().PaddingTop(14).PaddingBottom(4).Text(title).Bold().FontSize(BodyFontSize + 3);
    }

    /// <summary>
    /// An empty section says so in words. A blank space under a heading reads as "not exported"
    /// rather than "nothing happened", and those mean very different things to an auditor.
    /// </summary>
    private bool Empty(ColumnDescriptor body, int count)
    {
        if (count > 0) return false;

        body.Item().Text(Localizer["NothingRecordedInThisPeriodMSG"].Value).Italic().FontSize(BodyFontSize);

        return true;
    }

    /// <summary>
    /// A bordered table whose header row repeats on every page it spans. The widths are relative:
    /// they are the point widths the previous engine used, and they add up to more than the page is
    /// wide, which that engine let run into the margin and QuestPDF refuses — scaled, the proportions
    /// survive and every column stays on the page.
    /// </summary>
    private void AddTable(ColumnDescriptor body, float[] widths, IReadOnlyList<LocalizedString> headers,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        body.Item().DefaultTextStyle(style => style.FontSize(BodyFontSize - 1)).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                foreach (var width in widths)
                    columns.RelativeColumn(width);
            });

            table.Header(header =>
            {
                foreach (var title in headers)
                    Cell(header.Cell()).Text(title.Value).Bold();
            });

            foreach (var row in rows)
                foreach (var value in row)
                    Cell(table.Cell()).Text(value ?? "");
        });
    }

    private static IContainer Cell(IContainer cell) => cell.Border(0.25f).PaddingHorizontal(3).PaddingVertical(1);

    private static void Line(ColumnDescriptor lines, string label, string value)
    {
        lines.Item().Text(text =>
        {
            text.Span(label + ": ").Bold();
            text.Span(value);
        });
    }
}
