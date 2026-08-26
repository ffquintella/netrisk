using System.Globalization;
using Microsoft.Extensions.Localization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using Model.Governance;
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

    protected override async Task<Document> AddBody()
    {
        if (Document == null) throw new Exception("Document is null");

        return await Task.Run(() =>
        {
            if (ActiveSection == null) throw new Exception("ActiveSection is null");

            AddScope();

            AddAcceptances();
            AddReviews();
            AddCampaignDecisions();
            AddChanges();

            return Document;
        });
    }

    /// <summary>
    /// What the pack covers, who asked for it and when. An evidence file that does not state its own
    /// period cannot be filed against a finding — and the reader has no way to tell an empty section
    /// from a section that was never queried.
    /// </summary>
    private void AddScope()
    {
        var paragraph = ActiveSection!.AddParagraph();
        paragraph.Format.SpaceBefore = 10;
        paragraph.Format.Font.Size = BodyFontSize + 1;

        Line(paragraph, Localizer["Entity"], pack.EntityName);
        Line(paragraph, Localizer["Period"],
            $"{pack.FromUtc.ToString("yyyy-MM-dd", Iso)} → {pack.ToUtc.ToString("yyyy-MM-dd", Iso)} (UTC)");
        Line(paragraph, Localizer["GeneratedAt"], pack.GeneratedAtUtc.ToString("u", Iso));
        Line(paragraph, Localizer["RequestedBy"], pack.RequestedBy);
        Line(paragraph, Localizer["Contents"],
            $"{pack.Acceptances.Count} {Localizer["RiskAcceptances"]}, " +
            $"{pack.Reviews.Count} {Localizer["MgmtReviews"]}, " +
            $"{pack.CampaignDecisions.Count} {Localizer["BusinessReviewDecisions"]}, " +
            $"{pack.Changes.Count} {Localizer["FieldChanges"]}");

        if (pack.ChangesTruncated)
        {
            var warning = ActiveSection.AddParagraph();
            warning.Format.Font.Size = BodyFontSize;
            warning.AddFormattedText(Localizer["EvidenceTruncatedMSG"], TextFormat.Bold);
        }
    }

    private void AddAcceptances()
    {
        Heading(Localizer["RiskAcceptances"]);

        if (Empty(pack.Acceptances.Count)) return;

        foreach (var acceptance in pack.Acceptances)
        {
            var paragraph = ActiveSection!.AddParagraph();
            paragraph.Format.SpaceBefore = 6;
            paragraph.Format.Font.Size = BodyFontSize;

            paragraph.AddFormattedText($"#{acceptance.Id} {acceptance.Name}", TextFormat.Bold);
            paragraph.AddLineBreak();

            Line(paragraph, Localizer["Risk"],
                acceptance.RiskId == null ? "-" : $"#{acceptance.RiskId} {acceptance.RiskSubject}");
            Line(paragraph, Localizer["Status"], acceptance.Status);
            Line(paragraph, Localizer["AuthorizingManager"], acceptance.AuthorizingManager);

            if (acceptance.RequestedBy != null)
                Line(paragraph, Localizer["RequestedBy"], acceptance.RequestedBy);

            Line(paragraph, Localizer["Period"],
                $"{acceptance.StartDate.ToString("yyyy-MM-dd", Iso)} → " +
                $"{acceptance.ExpiresAt.ToString("yyyy-MM-dd", Iso)}");

            if (acceptance.ResidualScoreSnapshot != null)
                Line(paragraph, Localizer["ResidualRiskAtAcceptance"],
                    acceptance.ResidualScoreSnapshot.Value.ToString("0.00", Iso));

            if (acceptance.FromCampaign)
                Line(paragraph, Localizer["Origin"], Localizer["BusinessReviewCampaign"]);

            if (!string.IsNullOrWhiteSpace(acceptance.BusinessJustification))
                Line(paragraph, Localizer["BusinessJustification"], acceptance.BusinessJustification!);

            if (!string.IsNullOrWhiteSpace(acceptance.CompensatingControls))
                Line(paragraph, Localizer["CompensatingControls"], acceptance.CompensatingControls!);

            if (acceptance.RevokedAt != null)
                Line(paragraph, Localizer["Revoked"],
                    $"{acceptance.RevokedAt.Value.ToString("yyyy-MM-dd", Iso)} — " +
                    $"{acceptance.RevokedBy} — {acceptance.RevocationReason}");
        }
    }

    private void AddReviews()
    {
        Heading(Localizer["MgmtReviews"]);

        if (Empty(pack.Reviews.Count)) return;

        var table = NewTable([70, 55, 110, 110, 145]);

        HeaderRow(table, [Localizer["Date"], Localizer["Risk"], Localizer["Reviewer"],
            Localizer["Countersignature"], Localizer["Comments"]]);

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

            Row(table, [
                review.SubmissionDate.ToString("yyyy-MM-dd", Iso),
                $"#{review.RiskId}",
                review.Reviewer,
                countersignature,
                review.Comments
            ]);
        }
    }

    private void AddCampaignDecisions()
    {
        Heading(Localizer["BusinessReviewDecisions"]);

        if (Empty(pack.CampaignDecisions.Count)) return;

        var table = NewTable([100, 45, 30, 70, 90, 155]);

        HeaderRow(table, [Localizer["Campaign"], Localizer["Risk"], Localizer["BusinessRank"],
            Localizer["Decision"], Localizer["DecidedBy"], Localizer["Notes"]]);

        foreach (var decision in pack.CampaignDecisions)
        {
            var notes = decision.DecisionNotes ?? "";

            if (decision.EscalatedTo != null)
                notes = $"{Localizer["EscalatedTo"]}: {decision.EscalatedTo}. {notes}".Trim();

            if (decision.RiskAcceptanceId != null)
                notes = $"{Localizer["RiskAcceptance"]} #{decision.RiskAcceptanceId}. {notes}".Trim();

            Row(table, [
                $"{decision.CampaignName} ({decision.CampaignStatus})",
                $"#{decision.RiskId}",
                decision.Rank?.ToString(Iso) ?? "-",
                decision.Decision,
                decision.DecidedBy ?? "-",
                notes
            ]);
        }
    }

    private void AddChanges()
    {
        Heading(Localizer["FieldChanges"]);

        if (Empty(pack.Changes.Count)) return;

        var table = NewTable([80, 75, 70, 45, 75, 80, 65]);

        HeaderRow(table, [Localizer["When"], Localizer["Record"], Localizer["Field"],
            Localizer["Action"], Localizer["From"], Localizer["To"], Localizer["Actor"]]);

        foreach (var change in pack.Changes)
            Row(table, [
                change.OccurredAt.ToString("yyyy-MM-dd HH:mm", Iso),
                $"{change.EntityType} #{change.EntityId}",
                change.Field ?? "-",
                change.Action,
                Truncate(change.OldValue),
                Truncate(change.NewValue),
                change.Actor
            ]);
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

    private void Heading(string title)
    {
        var paragraph = ActiveSection!.AddParagraph();
        paragraph.Format.SpaceBefore = 14;
        paragraph.Format.SpaceAfter = 4;
        paragraph.Format.Font.Size = BodyFontSize + 3;
        paragraph.AddFormattedText(title, TextFormat.Bold);
    }

    /// <summary>
    /// An empty section says so in words. A blank space under a heading reads as "not exported"
    /// rather than "nothing happened", and those mean very different things to an auditor.
    /// </summary>
    private bool Empty(int count)
    {
        if (count > 0) return false;

        var paragraph = ActiveSection!.AddParagraph();
        paragraph.Format.Font.Size = BodyFontSize;
        paragraph.AddFormattedText(Localizer["NothingRecordedInThisPeriodMSG"], TextFormat.Italic);

        return true;
    }

    private Table NewTable(double[] widths)
    {
        var table = ActiveSection!.AddTable();
        table.Borders.Width = 0.25;
        table.Format.Font.Size = BodyFontSize - 1;

        foreach (var width in widths)
            table.AddColumn(Unit.FromPoint(width));

        return table;
    }

    private static void HeaderRow(Table table, IReadOnlyList<object> headers)
    {
        var row = table.AddRow();
        row.HeadingFormat = true;

        for (var i = 0; i < headers.Count; i++)
            row.Cells[i].AddParagraph().AddFormattedText(headers[i].ToString() ?? "", TextFormat.Bold);
    }

    private static void Row(Table table, IReadOnlyList<string> values)
    {
        var row = table.AddRow();

        for (var i = 0; i < values.Count; i++)
            row.Cells[i].AddParagraph(values[i] ?? "");
    }

    private static void Line(Paragraph paragraph, string label, string value)
    {
        paragraph.AddFormattedText(label + ": ", TextFormat.Bold);
        paragraph.AddText(value);
        paragraph.AddLineBreak();
    }
}
