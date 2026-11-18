using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Entities;

namespace Model.Assessments;

/// <summary>
/// The score of a submitted assessment run (M58, T280; S39 §3.1): <c>Σ risk score of the chosen option ÷ Σ highest risk
/// score of the question</c>, over the visible questions whose answer names an option. Negative scores count as 0, as in
/// the index's interim reading. Null when no answered question has a positive maximum — nothing to score, not a 0.
/// </summary>
public static class AssessmentRunScore
{
    /// <param name="visibleQuestionIds">The questions shown to the assessor when the run was submitted.</param>
    /// <param name="chosenOptionIds">The option each answered question names, by question id.</param>
    /// <param name="options">Every option of the assessment.</param>
    public static double? Compute(IEnumerable<int> visibleQuestionIds, IReadOnlyDictionary<int, int> chosenOptionIds,
        IEnumerable<AssessmentAnswer> options)
    {
        var byQuestion = options.GroupBy(o => o.QuestionId).ToDictionary(g => g.Key, g => g.ToList());

        double got = 0, max = 0;
        foreach (var questionId in visibleQuestionIds.Distinct())
        {
            if (!chosenOptionIds.TryGetValue(questionId, out var optionId)) continue;
            if (!byQuestion.TryGetValue(questionId, out var questionOptions)) continue;
            var chosen = questionOptions.FirstOrDefault(o => o.Id == optionId);
            if (chosen is null) continue;

            max += Math.Max(0, questionOptions.Max(o => (double)o.RiskScore));
            got += Math.Max(0, (double)chosen.RiskScore);
        }

        if (max <= 0) return null;
        return Math.Clamp(got / max, 0, 1);
    }
}
