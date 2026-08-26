using System.Globalization;
using System.Text;
using DAL.Entities;
using Model.Governance;

namespace API.Controllers;

/// <summary>
/// Renders the auditor evidence pack as CSV (Track 8 milestone 8.4.2, campaign evidence per 8.6.5).
///
/// Its own type rather than a method on the controller so it can be unit-tested without an HTTP
/// context: the escaping is the part that goes wrong, and a justification field containing a comma
/// and a newline is the normal case here, not an edge one.
///
/// The pack is written as four labelled sections in one file rather than four files, because an
/// auditor asks for "the evidence for Q3" and receives one attachment. Each section repeats its own
/// header row, which is what lets a spreadsheet user select one block and sort it.
/// </summary>
public static class GovernanceEvidenceCsv
{
    /// <summary>
    /// The change log on its own. Kept because the per-record trail view and the retention report
    /// both want exactly this and nothing else.
    /// </summary>
    public static string Render(IEnumerable<AuditLog> rows)
    {
        var builder = new StringBuilder();

        AppendChangeHeader(builder);

        foreach (var row in rows)
            AppendChange(builder, row.OccurredAt, row.EntityType, row.EntityId, row.Field,
                row.Action.ToString(), row.Actor, row.UserId, row.OldValue, row.NewValue,
                row.CorrelationId);

        return builder.ToString();
    }

    /// <summary>The whole pack: scope, acceptances, reviews, campaign decisions, then the trail.</summary>
    public static string Render(GovernanceEvidencePack pack)
    {
        var builder = new StringBuilder();

        // The scope block first. An evidence file that does not state its own period and requester
        // cannot be filed against a finding, and a reader has no way to tell an empty section from a
        // section that was never queried.
        builder.AppendLine("section,key,value");
        AppendScope(builder, "entity_id", pack.EntityId?.ToString(CultureInfo.InvariantCulture) ?? "");
        AppendScope(builder, "entity_name", pack.EntityName);
        AppendScope(builder, "period_from_utc", pack.FromUtc.ToString("O", CultureInfo.InvariantCulture));
        AppendScope(builder, "period_to_utc", pack.ToUtc.ToString("O", CultureInfo.InvariantCulture));
        AppendScope(builder, "generated_at_utc", pack.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        AppendScope(builder, "requested_by", pack.RequestedBy);
        AppendScope(builder, "changes_truncated", pack.ChangesTruncated ? "true" : "false");
        builder.AppendLine();

        builder.AppendLine("# risk_acceptances");
        builder.AppendLine("id,risk_id,risk_subject,name,status,authorizing_manager,requested_by," +
                           "start_date_utc,expires_at_utc,revoked_at_utc,revoked_by,revocation_reason," +
                           "business_justification,compensating_controls,residual_score_snapshot," +
                           "from_campaign");

        foreach (var a in pack.Acceptances)
            builder
                .Append(a.Id.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(a.RiskId?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',')
                .Append(Escape(a.RiskSubject)).Append(',')
                .Append(Escape(a.Name)).Append(',')
                .Append(Escape(a.Status)).Append(',')
                .Append(Escape(a.AuthorizingManager)).Append(',')
                .Append(Escape(a.RequestedBy)).Append(',')
                .Append(Iso(a.StartDate)).Append(',')
                .Append(Iso(a.ExpiresAt)).Append(',')
                .Append(Iso(a.RevokedAt)).Append(',')
                .Append(Escape(a.RevokedBy)).Append(',')
                .Append(Escape(a.RevocationReason)).Append(',')
                .Append(Escape(a.BusinessJustification)).Append(',')
                .Append(Escape(a.CompensatingControls)).Append(',')
                .Append(a.ResidualScoreSnapshot?.ToString("0.00", CultureInfo.InvariantCulture) ?? "")
                .Append(',')
                .Append(a.FromCampaign ? "true" : "false")
                .AppendLine();

        builder.AppendLine();

        builder.AppendLine("# mgmt_reviews");
        builder.AppendLine("id,risk_id,risk_subject,submitted_at_utc,reviewer,requires_countersignature," +
                           "second_reviewer,second_review_at_utc,segregation_override_reason,comments");

        foreach (var r in pack.Reviews)
            builder
                .Append(r.Id.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(r.RiskId.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Escape(r.RiskSubject)).Append(',')
                .Append(Iso(r.SubmissionDate)).Append(',')
                .Append(Escape(r.Reviewer)).Append(',')
                .Append(r.RequiresCountersignature ? "true" : "false").Append(',')
                .Append(Escape(r.SecondReviewer)).Append(',')
                .Append(Iso(r.SecondReviewAt)).Append(',')
                .Append(Escape(r.SegregationOverrideReason)).Append(',')
                .Append(Escape(r.Comments))
                .AppendLine();

        builder.AppendLine();

        builder.AppendLine("# business_review_decisions");
        builder.AppendLine("campaign_id,campaign_name,campaign_status,period_start_utc,period_end_utc," +
                           "due_date_utc,risk_id,risk_subject,business_rank,decision,decided_by," +
                           "decided_at_utc,escalated_to,risk_acceptance_id,decision_notes");

        foreach (var d in pack.CampaignDecisions)
            builder
                .Append(d.CampaignId.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Escape(d.CampaignName)).Append(',')
                .Append(Escape(d.CampaignStatus)).Append(',')
                .Append(Iso(d.PeriodStart)).Append(',')
                .Append(Iso(d.PeriodEnd)).Append(',')
                .Append(Iso(d.DueDate)).Append(',')
                .Append(d.RiskId.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Escape(d.RiskSubject)).Append(',')
                .Append(d.Rank?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',')
                .Append(Escape(d.Decision)).Append(',')
                .Append(Escape(d.DecidedBy)).Append(',')
                .Append(Iso(d.DecidedAt)).Append(',')
                .Append(Escape(d.EscalatedTo)).Append(',')
                .Append(d.RiskAcceptanceId?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',')
                .Append(Escape(d.DecisionNotes))
                .AppendLine();

        builder.AppendLine();

        builder.AppendLine("# field_changes");
        AppendChangeHeader(builder);

        foreach (var c in pack.Changes)
            AppendChange(builder, c.OccurredAt, c.EntityType, c.EntityId, c.Field, c.Action, c.Actor,
                c.UserId, c.OldValue, c.NewValue, c.CorrelationId);

        return builder.ToString();
    }

    private static void AppendScope(StringBuilder builder, string key, string value) =>
        builder.Append("scope,").Append(key).Append(',').AppendLine(Escape(value));

    private static void AppendChangeHeader(StringBuilder builder) =>
        builder.AppendLine("occurred_at_utc,entity_type,entity_id,field,action,actor,user_id," +
                           "old_value,new_value,correlation_id");

    private static void AppendChange(StringBuilder builder, DateTime occurredAt, string entityType,
        int entityId, string? field, string action, string actor, int? userId, string? oldValue,
        string? newValue, string? correlationId) =>
        builder
            .Append(occurredAt.ToString("O", CultureInfo.InvariantCulture)).Append(',')
            .Append(Escape(entityType)).Append(',')
            .Append(entityId.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(Escape(field)).Append(',')
            .Append(Escape(action)).Append(',')
            .Append(Escape(actor)).Append(',')
            .Append(userId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',')
            .Append(Escape(oldValue)).Append(',')
            .Append(Escape(newValue)).Append(',')
            .Append(Escape(correlationId))
            .AppendLine();

    private static string Iso(DateTime? value) =>
        value?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>
    /// RFC 4180 quoting, plus a formula guard.
    ///
    /// A justification beginning with <c>=</c>, <c>+</c>, <c>-</c> or <c>@</c> is executed as a
    /// formula by Excel and by Google Sheets when the file is opened. The evidence pack is a file
    /// that gets emailed to an auditor and opened in a spreadsheet, which is precisely the CSV
    /// injection scenario, so a leading tab is prepended to neutralise it while leaving the text
    /// readable.
    /// </summary>
    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        if (value[0] is '=' or '+' or '-' or '@') value = "\t" + value;

        if (value.IndexOfAny([',', '"', '\n', '\r', '\t']) < 0) return value;

        return '"' + value.Replace("\"", "\"\"") + '"';
    }
}
