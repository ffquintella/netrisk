using System.Globalization;
using DAL.Enums;
using Model.ThirdParties;

namespace Tools.ThirdParties;

/// <summary>What the findings read of a third party (S51 §4.7).</summary>
public sealed record ThirdPartyFacts(
    ThirdPartyStatus Status,
    bool? ProcessesPersonalData,
    DateTime? SubprocessorsDeclaredAt,
    int DataLocationCount,
    bool ProcessesADataRecord,
    bool? RightToAudit,
    string? ExitPlan,
    DateTime? ExitPlanTestedAt,
    string? DataPortability,
    int? ContractedRtoMinutes,
    int? ContractedRpoMinutes,
    int? VulnerabilityFixDays,
    DateTime? ContractEnd,
    decimal? SlaAvailabilityPercent,
    HecvatState Hecvat,
    int DependentCriticalProcessCount,
    ThirdPartyContinuityRequirementDto Requirement);

/// <summary>
/// The gaps in what the register knows about a third party (Stage 9.10, S51 §4.7, T202): the HECVAT, the LGPD
/// declarations (personal data, sub-processors, data location), the right to audit, the exit plan and portability, the
/// contracted RTO/RPO against what the BIA requires of what it supplies, the vulnerability-fix deadline FGV's NRM §5.2
/// caps at a month, the contract's end and the SLA. Computed on read; a finding is a signal, nothing is refused because of
/// one. A terminated relationship has none. Absent is a finding of its own, never read as compliant — an undeclared right
/// to audit is not a granted one.
/// </summary>
public static class ThirdPartyFindingsEvaluator
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static List<ThirdPartyFindingDto> Evaluate(ThirdPartyFacts facts, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var findings = new List<ThirdPartyFindingDto>();
        if (facts.Status == ThirdPartyStatus.Terminated) return findings;

        void Add(ThirdPartyFindingCode code, string message) => findings.Add(new ThirdPartyFindingDto { Code = code, Message = message });

        switch (facts.Hecvat)
        {
            case HecvatState.NotAssessed:
            case HecvatState.Voided:
                Add(ThirdPartyFindingCode.HecvatMissing, "No HECVAT is recorded for this supplier.");
                break;
            case HecvatState.Incomplete:
                Add(ThirdPartyFindingCode.HecvatIncomplete,
                    "The latest HECVAT is incomplete: it is not scored and does not count as conforming.");
                break;
            case HecvatState.NonConforming:
                Add(ThirdPartyFindingCode.HecvatNonConforming, "The latest HECVAT does not conform.");
                break;
            case HecvatState.Expired:
                Add(ThirdPartyFindingCode.HecvatExpired, "The latest HECVAT is past its validity date.");
                break;
            case HecvatState.NotScorable:
                Add(ThirdPartyFindingCode.HecvatNotScorable, "The latest HECVAT has nothing to score.");
                break;
        }

        if (facts.ProcessesPersonalData is null)
            Add(ThirdPartyFindingCode.PersonalDataUndeclared,
                "Whether this supplier processes personal data for the organization is not declared.");

        if (facts.ProcessesPersonalData == true && facts.SubprocessorsDeclaredAt is null)
            Add(ThirdPartyFindingCode.SubprocessorsUndeclared,
                "It processes personal data and its sub-processors were never declared — not even as none.");

        if ((facts.ProcessesPersonalData == true || facts.ProcessesADataRecord) && facts.DataLocationCount == 0)
            Add(ThirdPartyFindingCode.DataLocationMissing, "It processes the organization's data and no data location is declared.");

        if (facts.RightToAudit is null)
            Add(ThirdPartyFindingCode.RightToAuditUndeclared, "Whether the contract grants the right to audit is not declared.");
        else if (facts.RightToAudit == false)
            Add(ThirdPartyFindingCode.RightToAuditMissing, "The contract does not grant the organization the right to audit.");

        if (string.IsNullOrWhiteSpace(facts.ExitPlan))
            Add(ThirdPartyFindingCode.ExitPlanMissing, "There is no exit plan.");
        else if (facts.ExitPlanTestedAt is null && facts.DependentCriticalProcessCount > 0)
            Add(ThirdPartyFindingCode.ExitPlanUntested,
                $"It supports {facts.DependentCriticalProcessCount} critical process(es) and its exit plan was never exercised.");

        if (string.IsNullOrWhiteSpace(facts.DataPortability))
            Add(ThirdPartyFindingCode.DataPortabilityMissing, "How the organization's data comes back at the exit is not declared.");

        Objective(ThirdPartyFindingCode.RtoNotContracted, ThirdPartyFindingCode.RtoExceedsRequirement, "RTO",
            facts.ContractedRtoMinutes, facts.Requirement.RequiredRtoMinutes, facts.Requirement.RtoBindingName);
        Objective(ThirdPartyFindingCode.RpoNotContracted, ThirdPartyFindingCode.RpoExceedsRequirement, "RPO",
            facts.ContractedRpoMinutes, facts.Requirement.RequiredRpoMinutes, facts.Requirement.RpoBindingName);

        if (facts.VulnerabilityFixDays is null)
            Add(ThirdPartyFindingCode.VulnerabilityFixUndeclared,
                "No contracted deadline to fix a vulnerability of medium severity or higher (FGV NRM §5.2).");
        else if (facts.VulnerabilityFixDays > ThirdPartyLimits.NrmVulnerabilityFixDays)
            Add(ThirdPartyFindingCode.VulnerabilityFixTooSlow,
                $"The contracted deadline to fix a vulnerability of medium severity or higher is {facts.VulnerabilityFixDays} " +
                $"days; FGV's NRM §5.2 allows at most {ThirdPartyLimits.NrmVulnerabilityFixDays}.");

        if (facts.ContractEnd is { } end && end < now
                                         && facts.Status is ThirdPartyStatus.Active or ThirdPartyStatus.Exiting)
            Add(ThirdPartyFindingCode.ContractExpired,
                $"The contract ended on {end.ToString("yyyy-MM-dd", Invariant)} and the supplier is still {facts.Status}.");

        if (facts.SlaAvailabilityPercent is null)
            Add(ThirdPartyFindingCode.SlaUndeclared, "No contracted availability (SLA) is declared.");

        return findings;

        void Objective(ThirdPartyFindingCode notContracted, ThirdPartyFindingCode exceeds, string name, int? contracted,
            int? required, string? binding)
        {
            if (required is not { } need) return;

            var who = binding is null ? string.Empty : $" (set by {binding})";
            if (contracted is not { } has)
                Add(notContracted, $"What it supplies requires an {name} of {need} minute(s){who}, and the contract commits to none.");
            else if (has > need)
                Add(exceeds, $"The contracted {name} is {has} minute(s); what it supplies requires {need}{who}.");
        }
    }
}
