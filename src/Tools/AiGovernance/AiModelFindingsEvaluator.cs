using DAL.Enums;
using Model.AiGovernance;

namespace Tools.AiGovernance;

/// <summary>
/// What the findings read of an inventoried model (S53 §4.7). <paramref name="UncataloguedDataCount"/> counts the distinct
/// data records it uses that have no Stage 9.11 catalogue entry; <paramref name="LinkedRiskCount"/> counts every risk linked
/// to it, the ones the reader cannot see included, so a finding never depends on who asks.
/// </summary>
public sealed record AiModelFacts(
    AiModelStatus Status,
    AiModelSource Source,
    int? ThirdPartyId,
    int? OwnerId,
    AiModelRiskTier? RiskTier,
    AiHumanOversight? HumanOversight,
    DateTime? DataDeclaredAt,
    int UncataloguedDataCount,
    int LinkedRiskCount,
    AiModelEvaluationDto Evaluation);

/// <summary>
/// The gaps in a model's governance (Stage 9.12, S53 §4.7, T212–T215): the accountable owner, the declared risk tier and
/// oversight, the vendor, the data and its LGPD catalogue, the register's risks, and the evaluation of the current version.
///
/// Two rules shape it, as the catalogue's (S52 D3, D4). <b>Absent is never compliant</b>: an undeclared tier, oversight or
/// data list is a finding of its own, and a model in use with no recorded evaluation is <see cref="AiModelFindingCode.NotEvaluated"/>
/// — never a pass by omission (T215). <b>A finding is a signal</b>: nothing is refused because of one — the inventory records
/// what is, and the finding says what is missing. A proposed model is not yet in use, so the use findings (the register,
/// the evaluation) wait for its pilot; a retired one has none. Computed on read; pure.
/// </summary>
public static class AiModelFindingsEvaluator
{
    /// <summary>Pilot and production: the model is used.</summary>
    public static bool IsInUse(AiModelStatus status) => status is AiModelStatus.Pilot or AiModelStatus.Production;

    public static List<AiModelFindingDto> Evaluate(AiModelFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var findings = new List<AiModelFindingDto>();
        if (facts.Status == AiModelStatus.Retired) return findings;

        void Add(AiModelFindingCode code, string message) => findings.Add(new AiModelFindingDto { Code = code, Message = message });

        if (facts.OwnerId is null)
            Add(AiModelFindingCode.OwnerMissing,
                "No accountable owner: a model's owner is a person — the methodology's roles are human, never AI.");

        if (facts.RiskTier is null)
            Add(AiModelFindingCode.RiskTierUndeclared,
                "The risk tier is not declared; the model is held to the metrics a high-tier model reports.");

        if (facts.HumanOversight is null)
            Add(AiModelFindingCode.HumanOversightUndeclared,
                "Whether people review the model's outputs is not declared.");
        else if (facts.HumanOversight == AiHumanOversight.NoReview && facts.RiskTier is null or AiModelRiskTier.High)
            Add(AiModelFindingCode.HumanOversightAbsent,
                "Nobody reviews the outputs of a high-risk model: MIGR-TI/IA Phase 6 allows AI with a human in the loop.");

        if (facts.Source == AiModelSource.Vendor && facts.ThirdPartyId is null)
            Add(AiModelFindingCode.VendorUnregistered,
                "A vendor model whose vendor is not a registered third party (Stage 9.10).");

        if (facts.DataDeclaredAt is null)
            Add(AiModelFindingCode.DataUndeclared,
                "The data the model uses was never declared — declare an empty list if it uses no catalogued data.");
        else if (facts.UncataloguedDataCount > 0)
            Add(AiModelFindingCode.DataNotCatalogued,
                $"{facts.UncataloguedDataCount} data record(s) the model uses have no LGPD catalogue entry (Stage 9.11).");

        if (!IsInUse(facts.Status)) return findings;

        if (facts.LinkedRiskCount == 0)
            Add(AiModelFindingCode.NoRiskRegistered,
                "A model in use with no risk of the register linked: the risks of an AI component enter the same register.");

        var evaluation = facts.Evaluation;
        string Missing(AiMetricState state) => string.Join(", ", evaluation.Metrics
            .Where(m => m.Required && m.State == state)
            .Select(m => AiModelEvaluator.Label(m.Metric)));

        switch (evaluation.State)
        {
            case AiModelEvaluationState.NotEvaluated:
                Add(AiModelFindingCode.NotEvaluated,
                    $"Version '{evaluation.Version}' is in use with no recorded evaluation: {Missing(AiMetricState.NotEvaluated)} " +
                    "not evaluated. A model with no recorded evaluation is not evaluated.");
                break;
            case AiModelEvaluationState.Incomplete:
                Add(AiModelFindingCode.EvaluationIncomplete,
                    $"Version '{evaluation.Version}' has no reading of {Missing(AiMetricState.NotEvaluated)}.");
                break;
        }

        if (evaluation.Metrics.Any(m => m.Required && m.State == AiMetricState.Stale))
            Add(AiModelFindingCode.EvaluationStale,
                $"The latest reading of {Missing(AiMetricState.Stale)} is older than the model's maximum evaluation age.");

        return findings;
    }
}
