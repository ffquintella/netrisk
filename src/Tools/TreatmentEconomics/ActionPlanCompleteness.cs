using DAL.Enums;
using Model.TreatmentEconomics;

namespace Tools.TreatmentEconomics;

/// <summary>
/// What an action-plan line still lacks against MIGR-TI/IA Phase 5 — "specific action, owner, deadline, completion
/// evidence and acceptance criterion" (Stage 9.6, S47 §4.8). The action is the task's required title.
///
/// A completed task without evidence (one completed before schema 95, when none was asked for) reads as missing its
/// evidence, not as compliant. A cancelled task needs nothing.
/// </summary>
public static class ActionPlanCompleteness
{
    public static List<ActionPlanElement> Missing(bool hasOwner, bool hasDueDate, string? acceptanceCriterion,
        MitigationTaskStatus status, string? completionEvidence)
    {
        if (status == MitigationTaskStatus.Cancelled) return [];

        var missing = new List<ActionPlanElement>();
        if (!hasOwner) missing.Add(ActionPlanElement.Owner);
        if (!hasDueDate) missing.Add(ActionPlanElement.DueDate);
        if (string.IsNullOrWhiteSpace(acceptanceCriterion)) missing.Add(ActionPlanElement.AcceptanceCriterion);
        if (status == MitigationTaskStatus.Completed && string.IsNullOrWhiteSpace(completionEvidence))
            missing.Add(ActionPlanElement.CompletionEvidence);

        return missing;
    }
}
