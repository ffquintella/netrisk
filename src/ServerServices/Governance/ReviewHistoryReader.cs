using System.Globalization;
using System.Text.RegularExpressions;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;

namespace ServerServices.Governance;

/// <summary>
/// Reads the <see cref="ReviewHistory"/> of risks — reviews with their audit correlation, acceptances, campaign
/// decisions — through the context it is given, so under the caller's entity scope. Used by the overdue-review list
/// (T278); the risk index's fact reader shares <see cref="ReadAuditAsync"/>, so both recognise a derived review alike.
/// </summary>
public static class ReviewHistoryReader
{
    public static async Task<IReadOnlyDictionary<int, ReviewHistory>> ReadAsync(NRDbContext db,
        IReadOnlyCollection<int> riskIds, CancellationToken ct = default)
    {
        if (riskIds.Count == 0) return new Dictionary<int, ReviewHistory>();
        var ids = riskIds.ToList();

        var risks = await db.Risks.AsNoTracking().Where(r => ids.Contains(r.Id))
            .Select(r => new { r.Id, r.SubmissionDate, r.SubmittedBy, r.Owner, r.Manager })
            .ToListAsync(ct);

        var reviews = await db.MgmtReviews.AsNoTracking().Where(m => ids.Contains(m.RiskId))
            .Select(m => new { m.Id, m.RiskId, m.Reviewer, m.SubmissionDate })
            .ToListAsync(ct);

        var acceptances = await db.RiskAcceptances.AsNoTracking()
            .Where(a => a.RiskId != null && ids.Contains(a.RiskId.Value))
            .Select(a => new
            {
                RiskId = a.RiskId!.Value, a.CreatedAt, a.ExpiresAt, a.RevokedAt, a.CreatedById, a.RequestedById,
                a.AuthorizingManagerId
            })
            .ToListAsync(ct);

        var decisions = await db.RiskReviewCampaignItems.AsNoTracking()
            .Where(i => ids.Contains(i.RiskId) && i.DecidedAt != null)
            .Select(i => new { i.RiskId, DecidedAt = i.DecidedAt!.Value, i.DecidedById })
            .ToListAsync(ct);

        var audit = await ReadAuditAsync(db, ct);

        // Grouped once rather than filtered per risk; a lookup keeps each group in the order the rows were read.
        var reviewsByRisk = reviews.ToLookup(m => m.RiskId);
        var acceptancesByRisk = acceptances.ToLookup(a => a.RiskId);
        var decisionsByRisk = decisions.ToLookup(d => d.RiskId);

        return risks.ToDictionary(r => r.Id, r => new ReviewHistory
        {
            SubmissionDate = r.SubmissionDate,
            SubmittedBy = r.SubmittedBy,
            Owner = r.Owner,
            Manager = r.Manager,
            Reviews = reviewsByRisk[r.Id]
                .Select(m => audit.Evidence(m.Id, m.RiskId, m.Reviewer, m.SubmissionDate)).ToList(),
            Acceptances = acceptancesByRisk[r.Id].Select(a => new AcceptanceEvidence
            {
                CreatedAt = a.CreatedAt, End = a.RevokedAt ?? a.ExpiresAt, CreatedById = a.CreatedById,
                RequestedById = a.RequestedById, AuthorizingManagerId = a.AuthorizingManagerId
            }).ToList(),
            Decisions = decisionsByRisk[r.Id]
                .Select(d => new CampaignDecisionEvidence(d.DecidedAt, d.DecidedById)).ToList()
        });
    }

    /// <summary>The creation row of a management review in <c>audit_logs</c>.</summary>
    public sealed record ReviewCreationRow(int EntityId, string? NewValue, DateTime OccurredAt, string? CorrelationId);

    /// <summary>What the audit trail says about review creations and the saves that create acceptances or decide items.</summary>
    public sealed class ReviewAudit
    {
        public required IReadOnlyList<ReviewCreationRow> ReviewCreations { get; init; }
        public required IReadOnlySet<string> AcceptanceCreations { get; init; }
        public required IReadOnlySet<string> CampaignDecisions { get; init; }

        private CreationIndex? _index;

        /// <summary>
        /// The creation row of a review. A created row is keyed by EF's temporary id in production (the interceptor runs
        /// before the insert), so the row is matched by its id when it is real and otherwise by its recorded RiskId,
        /// Reviewer and SubmissionDate (within a second). The earliest matching row wins; the first listed wins a tie.
        /// </summary>
        public ReviewCreationRow? ReviewCreation(int id, int riskId, int reviewer, DateTime submission) =>
            LazyInitializer.EnsureInitialized(ref _index, () => new CreationIndex(ReviewCreations))
                .Find(id, riskId, reviewer, submission);

        /// <summary>A review as the rule reads it: its creation row's time and correlation, and what else that save did.</summary>
        public ReviewEvidence Evidence(int id, int riskId, int reviewer, DateTime submission)
        {
            var row = ReviewCreation(id, riskId, reviewer, submission);
            return new ReviewEvidence
            {
                Id = id,
                ReviewerId = reviewer,
                SubmissionDate = submission,
                AuditedAt = row?.OccurredAt,
                CorrelationId = row?.CorrelationId,
                SavedWithAcceptance = row?.CorrelationId is { } c1 && AcceptanceCreations.Contains(c1),
                SavedWithCampaignDecision = row?.CorrelationId is { } c2 && CampaignDecisions.Contains(c2)
            };
        }
    }

    /// <summary>
    /// The creation rows read once (M58, T278): by entity id, and the temporary-id rows by the RiskId and Reviewer their
    /// summary records, with its SubmissionDate parsed. Matching every review against every row, three regexes a row,
    /// cost reviews × rows. Each row keeps its position, so a tie on time resolves as the ordered scan did.
    /// </summary>
    private sealed class CreationIndex
    {
        private readonly ReviewCreationRow[] _rows;
        private readonly Dictionary<int, List<int>> _byId = new();
        private readonly Dictionary<(string RiskId, string Reviewer), List<(int Position, DateTime SubmittedAt)>> _byFields = new();

        public CreationIndex(IEnumerable<ReviewCreationRow> rows)
        {
            _rows = rows.ToArray();

            for (var position = 0; position < _rows.Length; position++)
            {
                var row = _rows[position];

                if (!_byId.TryGetValue(row.EntityId, out var sameId)) _byId[row.EntityId] = sameId = [];
                sameId.Add(position);

                if (row.EntityId > 0 || Field(row.NewValue, "RiskId") is not { } riskId ||
                    Field(row.NewValue, "Reviewer") is not { } reviewer ||
                    !DateTime.TryParse(Field(row.NewValue, "SubmissionDate"), CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var submittedAt))
                    continue;

                if (!_byFields.TryGetValue((riskId, reviewer), out var sameFields))
                    _byFields[(riskId, reviewer)] = sameFields = [];
                sameFields.Add((position, submittedAt));
            }
        }

        public ReviewCreationRow? Find(int id, int riskId, int reviewer, DateTime submission)
        {
            var best = -1;

            void Consider(int position)
            {
                if (best < 0 || _rows[position].OccurredAt < _rows[best].OccurredAt ||
                    (_rows[position].OccurredAt == _rows[best].OccurredAt && position < best))
                    best = position;
            }

            if (_byId.TryGetValue(id, out var sameId))
                foreach (var position in sameId) Consider(position);

            var fields = (riskId.ToString(CultureInfo.InvariantCulture), reviewer.ToString(CultureInfo.InvariantCulture));
            if (_byFields.TryGetValue(fields, out var sameFields))
                foreach (var (position, submittedAt) in sameFields)
                    if (Math.Abs((submittedAt - submission).TotalSeconds) <= 1)
                        Consider(position);

            return best < 0 ? null : _rows[best];
        }
    }

    public static async Task<ReviewAudit> ReadAuditAsync(NRDbContext db, CancellationToken ct = default)
    {
        var reviewCreations = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == nameof(MgmtReview) && a.Action == AuditLogAction.Create)
            .Select(a => new ReviewCreationRow(a.EntityId, a.NewValue, a.OccurredAt, a.CorrelationId))
            .ToListAsync(ct);

        var acceptanceCreations = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == nameof(RiskAcceptance) && a.Action == AuditLogAction.Create && a.CorrelationId != null)
            .Select(a => a.CorrelationId!).Distinct().ToListAsync(ct);

        var campaignDecisions = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == nameof(RiskReviewCampaignItem) && a.Action == AuditLogAction.Update &&
                        a.Field == nameof(RiskReviewCampaignItem.Decision) && a.CorrelationId != null)
            .Select(a => a.CorrelationId!).Distinct().ToListAsync(ct);

        return new ReviewAudit
        {
            ReviewCreations = reviewCreations,
            AcceptanceCreations = acceptanceCreations.ToHashSet(StringComparer.Ordinal),
            CampaignDecisions = campaignDecisions.ToHashSet(StringComparer.Ordinal)
        };
    }

    /// <summary>A field of the interceptor's <c>name=value; …</c> summary of a created row.</summary>
    public static string? Field(string? summary, string name)
    {
        if (string.IsNullOrEmpty(summary)) return null;
        var match = Regex.Match(summary, $@"(?:^|; ){Regex.Escape(name)}=([^;]*)(?:;|$)");
        return match.Success ? match.Groups[1].Value : null;
    }
}
