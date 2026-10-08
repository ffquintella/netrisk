using System.Globalization;
using DAL.Enums;
using Model.ThirdParties;

namespace Tools.ThirdParties;

/// <summary>One recorded HECVAT answer, as the scorer sees it.</summary>
public sealed record HecvatAnswerFacts(
    string QuestionId,
    HecvatAnswer Answer,
    HecvatAnswer? PreferredAnswer,
    int Weight,
    bool Critical);

/// <summary>
/// Scores a HECVAT questionnaire (Stage 9.10, S51 §4.5, T202, T205), on read and never stored.
///
/// <b>A partial questionnaire is <see cref="HecvatState.Incomplete"/>, never a pass</b> (S27's edge case for this
/// stage; S51 D7). How many questions the vendor was asked is declared with the assessment, so "partial" is measured,
/// not guessed: fewer distinct questions answered Yes, No or N/A than expected, or any question recorded blank, is
/// incomplete — and an incomplete questionnaire reports <em>no</em> score, because the share of the answered part would
/// read as the score of the whole. The obvious implementation scores the answers it has; that is the defect this class
/// exists to avoid.
///
/// A complete one is scored over the questions with a preferred answer, N/A excluded: the weight answered as preferred
/// over the weight answered at all. It conforms at <see cref="ThirdPartyLimits.HecvatPassThreshold"/> or above with no
/// critical question answered against the preference. Past its validity date it is <see cref="HecvatState.Expired"/>,
/// and with nothing scorable it is <see cref="HecvatState.NotScorable"/> — neither is a pass.
/// </summary>
public static class HecvatScoring
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static HecvatResultDto Score(int expectedCount, IReadOnlyCollection<HecvatAnswerFacts> answers,
        DateTime? validUntil, bool voided, DateTime now, decimal passThreshold = ThirdPartyLimits.HecvatPassThreshold)
    {
        ArgumentNullException.ThrowIfNull(answers);

        // One answer per question id, case-insensitively; the service already guarantees it, the scorer does not rely on it.
        var distinct = answers
            .GroupBy(a => a.QuestionId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var answered = distinct.Where(a => a.Answer is HecvatAnswer.Yes or HecvatAnswer.No or HecvatAnswer.NotApplicable)
            .ToList();
        var blank = distinct.Where(a => a.Answer == HecvatAnswer.Unanswered)
            .Select(a => a.QuestionId.Trim().ToUpperInvariant())
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        var result = new HecvatResultDto
        {
            ExpectedCount = expectedCount,
            AnsweredCount = answered.Count,
            NotApplicableCount = answered.Count(a => a.Answer == HecvatAnswer.NotApplicable),
            UnansweredCount = System.Math.Max(0, expectedCount - answered.Count),
            PassThreshold = passThreshold,
            BlankQuestionIds = blank.Take(ThirdPartyLimits.MaxListedQuestionIds).ToList()
        };

        if (voided)
        {
            result.State = HecvatState.Voided;
            result.Explanation = "This assessment was voided; it is kept as evidence and read as nothing.";
            return result;
        }

        if (answered.Count < expectedCount || blank.Count > 0)
        {
            result.State = HecvatState.Incomplete;
            result.Explanation =
                $"{answered.Count} of {expectedCount} question(s) answered" +
                (blank.Count > 0 ? $", {blank.Count} recorded blank" : string.Empty) +
                ". An incomplete HECVAT is not scored and never passes.";
            return result;
        }

        var scored = answered
            .Where(a => a.Answer != HecvatAnswer.NotApplicable && a.PreferredAnswer is HecvatAnswer.Yes or HecvatAnswer.No)
            .ToList();
        var scoredWeight = scored.Sum(a => (decimal)a.Weight);

        result.CriticalFailures = scored
            .Where(a => a.Critical && a.Answer != a.PreferredAnswer)
            .Select(a => a.QuestionId.Trim().ToUpperInvariant())
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        if (scoredWeight > 0)
            result.Score = System.Math.Round(scored.Where(a => a.Answer == a.PreferredAnswer).Sum(a => (decimal)a.Weight) / scoredWeight,
                4, MidpointRounding.AwayFromZero);

        if (validUntil is { } until && until < now)
        {
            result.State = HecvatState.Expired;
            result.Explanation = $"Complete, but valid only until {until.ToString("yyyy-MM-dd", Invariant)}: an expired " +
                                 "HECVAT never passes. Ask the vendor for a current one.";
            return result;
        }

        if (result.Score is not { } score)
        {
            result.State = HecvatState.NotScorable;
            result.Explanation = "Complete, but no answered question has a preferred answer to score against — " +
                                 "nothing here can conform.";
            return result;
        }

        if (result.CriticalFailures.Count > 0)
        {
            result.State = HecvatState.NonConforming;
            result.Explanation = $"{result.CriticalFailures.Count} critical question(s) answered against the preference " +
                                 $"({string.Join(", ", result.CriticalFailures)}): non-conforming whatever the score " +
                                 $"({Percent(score)}).";
            return result;
        }

        result.State = score >= passThreshold ? HecvatState.Conforming : HecvatState.NonConforming;
        result.Explanation = $"Scored {Percent(score)} of the weight answered as preferred, against a pass threshold of " +
                             $"{Percent(passThreshold)}.";
        return result;
    }

    private static string Percent(decimal ratio) => (ratio * 100m).ToString("0.##", Invariant) + " %";
}
