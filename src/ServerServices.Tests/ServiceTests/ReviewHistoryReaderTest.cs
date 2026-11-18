using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using JetBrains.Annotations;
using ServerServices.Governance;
using Xunit;
using static ServerServices.Governance.ReviewHistoryReader;

namespace ServerServices.Tests.ServiceTests;

/// <summary>
/// M58, T278: matching a management review to its creation row in <c>audit_logs</c>. The rows are indexed once per
/// <see cref="ReviewAudit"/> rather than scanned (and regex-parsed) once per review, and the match is unchanged: the
/// real id, or a temporary-id row naming the same risk, reviewer and submission time within a second; the earliest row
/// wins, and the first in the list wins a tie.
/// </summary>
[TestSubject(typeof(ReviewHistoryReader))]
public class ReviewHistoryReaderTest
{
    private static readonly DateTime Base = new(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);

    private static string Summary(int riskId, int reviewer, DateTime submission) =>
        $"Id=0; RiskId={riskId}; Reviewer={reviewer}; SubmissionDate={submission.ToString("O", CultureInfo.InvariantCulture)}; Comments=x";

    private sealed record Review(int Id, int RiskId, int Reviewer, DateTime Submission);

    /// <summary>
    /// Many reviews over few risks, with every shape the matcher distinguishes: a real id, a temporary id with matching
    /// fields, a submission time half a second and a second and a half off, an unparsable date, a missing field, a real id
    /// that also carries matching fields, and several rows tied on their time.
    /// </summary>
    private static (List<Review> Reviews, List<ReviewCreationRow> Rows) ManyReviews()
    {
        var reviews = new List<Review>();
        var rows = new List<ReviewCreationRow>();

        for (var i = 1; i <= 240; i++)
        {
            var riskId = 1 + i % 17;
            var reviewer = 1 + i % 5;
            var submission = Base.AddMinutes(i * 7 % 61);
            var occurred = Base.AddHours(i % 9);
            reviews.Add(new Review(i, riskId, reviewer, submission));

            switch (i % 8)
            {
                case 0:
                    rows.Add(new ReviewCreationRow(i, Summary(riskId, reviewer, submission), occurred, $"real-{i}"));
                    break;
                case 1:
                    rows.Add(new ReviewCreationRow(-i, Summary(riskId, reviewer, submission.AddMilliseconds(500)), occurred,
                        $"temp-{i}"));
                    break;
                case 2:
                    rows.Add(new ReviewCreationRow(-i, Summary(riskId, reviewer, submission.AddMilliseconds(1500)), occurred,
                        $"late-{i}"));
                    break;
                case 3:
                    rows.Add(new ReviewCreationRow(-i, $"RiskId={riskId}; Reviewer={reviewer}; SubmissionDate=not-a-date",
                        occurred, $"garbled-{i}"));
                    break;
                case 4:
                    rows.Add(new ReviewCreationRow(-i, $"RiskId={riskId}; SubmissionDate={submission:O}", occurred,
                        $"partial-{i}"));
                    break;
                case 5:
                    // Tied on time with the real-id row below: the earlier one in the list must win.
                    rows.Add(new ReviewCreationRow(0, Summary(riskId, reviewer, submission), Base, $"tie-a-{i}"));
                    rows.Add(new ReviewCreationRow(i, null, Base, $"tie-b-{i}"));
                    break;
                case 6:
                    // A real id carrying the fields of another review: matches only by its id.
                    rows.Add(new ReviewCreationRow(i, Summary(riskId, reviewer, submission.AddMinutes(-7)), occurred,
                        $"fields-{i}"));
                    break;
                case 7:
                    // The id row is later than a temporary-id row for the same review: the earlier one wins.
                    rows.Add(new ReviewCreationRow(i, null, occurred.AddHours(1), $"id-late-{i}"));
                    rows.Add(new ReviewCreationRow(-i, Summary(riskId, reviewer, submission), occurred, $"temp-early-{i}"));
                    break;
            }
        }

        // Rows nothing claims, and a second, later creation row for a review that already has one.
        rows.Add(new ReviewCreationRow(10_000, Summary(1, 1, Base), Base, "orphan"));
        rows.Add(new ReviewCreationRow(8, Summary(1 + 8 % 17, 1 + 8 % 5, Base.AddMinutes(8 * 7 % 61)), Base.AddDays(1), "later"));

        return (reviews, rows);
    }

    /// <summary>The matcher as it was before the index, applied to the same rows: the reference result.</summary>
    private static ReviewCreationRow? Reference(IReadOnlyList<ReviewCreationRow> rows, int id, int riskId, int reviewer,
        DateTime submission) =>
        rows.Where(r => r.EntityId == id ||
                        r.EntityId <= 0 && Field(r.NewValue, "RiskId") == riskId.ToString(CultureInfo.InvariantCulture) &&
                        Field(r.NewValue, "Reviewer") == reviewer.ToString(CultureInfo.InvariantCulture) &&
                        DateTime.TryParse(Field(r.NewValue, "SubmissionDate"), CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out var at) &&
                        Math.Abs((at - submission).TotalSeconds) <= 1)
            .OrderBy(r => r.OccurredAt).FirstOrDefault();

    private static ReviewAudit Audit(IReadOnlyList<ReviewCreationRow> rows) => new()
    {
        ReviewCreations = rows,
        AcceptanceCreations = new HashSet<string>(["temp-1", "real-8"], StringComparer.Ordinal),
        CampaignDecisions = new HashSet<string>(["tie-a-5"], StringComparer.Ordinal)
    };

    [Fact]
    public void TestManyReviewsMatchExactlyTheRowsTheUnindexedScanMatched()
    {
        var (reviews, rows) = ManyReviews();
        var audit = Audit(rows);

        var matched = 0;
        foreach (var review in reviews)
        {
            var expected = Reference(rows, review.Id, review.RiskId, review.Reviewer, review.Submission);

            Assert.Equal(expected, audit.ReviewCreation(review.Id, review.RiskId, review.Reviewer, review.Submission));

            var evidence = audit.Evidence(review.Id, review.RiskId, review.Reviewer, review.Submission);
            Assert.Equal((expected?.OccurredAt, expected?.CorrelationId), (evidence.AuditedAt, evidence.CorrelationId));
            Assert.Equal(expected?.CorrelationId is "temp-1" or "real-8", evidence.SavedWithAcceptance);
            Assert.Equal(expected?.CorrelationId is "tie-a-5", evidence.SavedWithCampaignDecision);

            if (expected != null) matched++;
        }

        // The fixture exercises both outcomes, so an index that matched nothing (or everything) would not pass.
        Assert.InRange(matched, reviews.Count / 3, reviews.Count - reviews.Count / 4);
        Assert.Equal("tie-a-5", audit.ReviewCreation(5, 1 + 5 % 17, 1 + 5 % 5, Base.AddMinutes(5 * 7 % 61))?.CorrelationId);
        Assert.Equal("real-8", audit.ReviewCreation(8, 1 + 8 % 17, 1 + 8 % 5, Base.AddMinutes(8 * 7 % 61))?.CorrelationId);
        Assert.Equal("temp-early-7", audit.ReviewCreation(7, 1 + 7 % 17, 1 + 7 % 5, Base.AddMinutes(7 * 7 % 61))?.CorrelationId);
        Assert.Null(audit.ReviewCreation(2, 1 + 2 % 17, 1 + 2 % 5, Base.AddMinutes(2 * 7 % 61)));
    }

    /// <summary>
    /// The defect: every review re-read every creation row and ran three regexes over it, so reading the history of n
    /// reviews against m rows cost n × m. The rows are now read once, however many reviews are matched.
    /// </summary>
    [Fact]
    public void TestTheCreationRowsAreReadOnceWhateverTheNumberOfReviews()
    {
        var (reviews, rows) = ManyReviews();
        var counted = new CountingList<ReviewCreationRow>(rows);
        var audit = Audit(counted);

        foreach (var review in reviews)
            audit.Evidence(review.Id, review.RiskId, review.Reviewer, review.Submission);

        Assert.InRange(counted.Reads, 1, rows.Count);
    }

    /// <summary>A read-only list that counts every element handed out, through the enumerator or the indexer.</summary>
    private sealed class CountingList<T>(IReadOnlyList<T> inner) : IReadOnlyList<T>
    {
        public int Reads { get; private set; }

        public int Count => inner.Count;

        public T this[int index]
        {
            get
            {
                Reads++;
                return inner[index];
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            foreach (var item in inner)
            {
                Reads++;
                yield return item;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
