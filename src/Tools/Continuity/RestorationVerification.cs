using DAL.Enums;
using Model.Continuity;

namespace Tools.Continuity;

/// <summary>The facts of one restoration test the verification reads.</summary>
public sealed record RestorationTestFacts(
    int Id,
    DateTime TestedAt,
    RestorationTestOutcome Outcome,
    int? AchievedRtoMinutes,
    int? AchievedRpoMinutes,
    bool IsVoided)
{
    public int? Achieved(ContinuityObjective objective) =>
        objective == ContinuityObjective.Rto ? AchievedRtoMinutes : AchievedRpoMinutes;
}

/// <summary>
/// Whether a declared RTO or RPO is verified by a restoration test (Stage 9.3, S43 §4.6, T159–T160).
///
/// <list type="number">
/// <item>Not declared → <see cref="ObjectiveVerificationStatus.Absent"/>, whatever the tests say.</item>
/// <item>The candidates are the tests not voided that failed or measured this objective. No test not
/// voided → <c>Unverified/NoTest</c>; tests but no candidate → <c>Unverified/NotMeasured</c>.</item>
/// <item>The most recent candidate decides (tested-at, then the higher id) — never the best one ever,
/// which would let an old pass hide a recent failure. Older than the validity → <c>Unverified/Stale</c>
/// (the bound is inclusive).</item>
/// <item>Failed → <c>NotMet/RestorationFailed</c>; measure ≤ declared → <c>Met</c>; above →
/// <c>NotMet/Exceeded</c>.</item>
/// </list>
///
/// So a declared RTO with no test reads "unverified", never "met" — the difference between the
/// methodology's metric measuring something and measuring the intention.
/// </summary>
public static class RestorationVerification
{
    public static ObjectiveVerificationDto Evaluate(ContinuityObjective objective, int? declared,
        IEnumerable<RestorationTestFacts> tests, DateTime nowUtc, int validityDays)
    {
        var result = new ObjectiveVerificationDto
        {
            Objective = objective,
            DeclaredMinutes = declared,
            ValidityDays = validityDays
        };

        if (declared is null)
        {
            result.Status = ObjectiveVerificationStatus.Absent;
            return result;
        }

        var live = tests.Where(t => !t.IsVoided).ToList();

        if (live.Count == 0)
            return Unverified(result, VerificationReason.NoTest);

        var decisive = live
            .Where(t => t.Outcome == RestorationTestOutcome.Failed || t.Achieved(objective) is not null)
            .OrderByDescending(t => t.TestedAt)
            .ThenByDescending(t => t.Id)
            .FirstOrDefault();

        if (decisive is null)
            return Unverified(result, VerificationReason.NotMeasured);

        result.TestId = decisive.Id;
        result.TestedAt = decisive.TestedAt;
        result.ValidUntil = decisive.TestedAt.AddDays(validityDays);
        result.AchievedMinutes = decisive.Achieved(objective);

        if (decisive.TestedAt < nowUtc.AddDays(-validityDays))
            return Unverified(result, VerificationReason.Stale);

        if (decisive.Outcome == RestorationTestOutcome.Failed)
        {
            result.Status = ObjectiveVerificationStatus.NotMet;
            result.Reason = VerificationReason.RestorationFailed;
            return result;
        }

        if (decisive.Achieved(objective) <= declared)
        {
            result.Status = ObjectiveVerificationStatus.Met;
            return result;
        }

        result.Status = ObjectiveVerificationStatus.NotMet;
        result.Reason = VerificationReason.Exceeded;
        return result;
    }

    private static ObjectiveVerificationDto Unverified(ObjectiveVerificationDto result, VerificationReason reason)
    {
        result.Status = ObjectiveVerificationStatus.Unverified;
        result.Reason = reason;
        return result;
    }
}
