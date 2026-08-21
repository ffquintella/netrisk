namespace Model.Risks;

public class RiskHelper
{
    public static string GetRiskStatusName(RiskStatus riskStatus)
    {
        return riskStatus switch
        {
            RiskStatus.New => "New",
            RiskStatus.MitigationPlanned => "Mitigation Planned",
            RiskStatus.ManagementReview => "Mgmt Reviewed",
            RiskStatus.Closed => "Closed",
            _ => throw new ArgumentOutOfRangeException(nameof(riskStatus), riskStatus, null)
        };
    }

    /// <summary>
    /// Maps a <c>next_step</c> value recorded on a management review to the risk-lifecycle stage
    /// that carries it out, so the review's chosen next step can actually be offered to the user
    /// (docs/ux-interaction-standard.md IX-6) instead of being captured and ignored.
    ///
    /// The values are the seeded <c>next_step</c> rows (see <c>DB/Data/1.sql</c>):
    /// 1 "Accept until Next Review", 2 "Consider for Project", 3 "Submit as a Production Issue",
    /// 4 "Reject". Only 2 and 4 imply an immediate next stage inside NetRisk; acceptance and
    /// production-issue submission are handled outside the risk lifecycle, so they map to
    /// <see cref="RiskNextStepAction.None"/> rather than guessing at a stage.
    /// </summary>
    public static RiskNextStepAction GetNextStepAction(int nextStep)
    {
        return nextStep switch
        {
            2 => RiskNextStepAction.PlanMitigation,
            4 => RiskNextStepAction.CloseRisk,
            _ => RiskNextStepAction.None
        };
    }

    /// <summary>
    /// Inverse of <see cref="GetRiskStatusName"/>: maps a legacy free-text <c>risks.status</c> value to its
    /// <see cref="RiskStatus"/>, or <c>null</c> for any value outside the known set. This is the documented,
    /// testable mirror of the Track 6 Phase 5 SQL backfill of <c>risks.status_id</c> (New=0, Mitigation
    /// Planned=1, Mgmt Reviewed=2, Closed=3); unknown legacy values stay NULL rather than defaulting to New.
    /// </summary>
    public static RiskStatus? GetRiskStatusFromName(string? statusName)
    {
        return statusName switch
        {
            "New" => RiskStatus.New,
            "Mitigation Planned" => RiskStatus.MitigationPlanned,
            "Mgmt Reviewed" => RiskStatus.ManagementReview,
            "Closed" => RiskStatus.Closed,
            _ => null
        };
    }
}