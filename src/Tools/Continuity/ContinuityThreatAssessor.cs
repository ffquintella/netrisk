using Model.Continuity;

namespace Tools.Continuity;

/// <summary>
/// The weighted threat to a node's RTO/RPO — the flag 4 basis Stage 9.5 consumes (Stage 9.3, S43 §4.6,
/// D16).
///
/// Per objective, with <c>own</c> the value the node requires of itself (RTO, or MTPD without an RTO;
/// RPO with no fallback):
/// <list type="bullet">
/// <item><b>Confirmed (weight 1.0):</b> the node's objective is not met; it is looser than its
/// dependents require; a provider's is not met; a provider declares more than <c>own</c>.</item>
/// <item><b>Not verified (the configured weight, default 0.5):</b> the node's objective has no valid
/// test; a dependent requires an objective the node does not declare; a provider's is unverified; a
/// provider declares nothing.</item>
/// </list>
/// Inherited items walk the providers with the graph's iterative walk, so a cycle neither repeats nor
/// loops; a node without <c>own</c> inherits nothing (absent is not 0). The node's weight is the
/// <b>highest</b> item weight — two unverified items do not add up to a confirmed one.
/// </summary>
public static class ContinuityThreatAssessor
{
    public const decimal ConfirmedWeight = 1.0m;

    public static ContinuityThreatDto Assess(ContinuityGraph graph, int nodeId,
        Func<int, ContinuityObjective, ObjectiveVerificationStatus> statusOf, decimal unverifiedWeight)
    {
        var dto = new ContinuityThreatDto { UnverifiedWeight = unverifiedWeight };

        var node = graph.Find(nodeId);
        if (node is null) return dto;

        var items = new Dictionary<(ContinuityThreatReason, ContinuityObjective, int?), ContinuityThreatItemDto>();

        void Add(ContinuityThreatReason reason, ContinuityObjective objective, ContinuityThreatClass @class, int? via)
        {
            var key = (reason, objective, via);
            if (items.ContainsKey(key)) return;

            items[key] = new ContinuityThreatItemDto
            {
                Reason = reason, Objective = objective, Class = @class,
                Weight = @class == ContinuityThreatClass.Confirmed ? ConfirmedWeight : unverifiedWeight,
                ViaEntityId = via, ViaName = via is { } id ? graph.Find(id)?.Name : null
            };
        }

        var providers = graph.Providers(nodeId).OrderBy(p => p.Value).ThenBy(p => p.Key).Select(p => p.Key).ToList();

        foreach (var objective in new[] { ContinuityObjective.Rto, ContinuityObjective.Rpo })
        {
            switch (statusOf(nodeId, objective))
            {
                case ObjectiveVerificationStatus.NotMet:
                    Add(ContinuityThreatReason.NotMet, objective, ContinuityThreatClass.Confirmed, null);
                    break;
                case ObjectiveVerificationStatus.Unverified:
                    Add(ContinuityThreatReason.Unverified, objective, ContinuityThreatClass.NotVerified, null);
                    break;
            }

            if (graph.HasConflict(nodeId, objective))
                Add(ContinuityThreatReason.CascadeConflict, objective, ContinuityThreatClass.Confirmed, null);

            if (graph.HasRequirementWithoutObjective(nodeId, objective))
                Add(ContinuityThreatReason.RequirementWithoutObjective, objective, ContinuityThreatClass.NotVerified, null);

            if (node.Requires(objective) is not { } own) continue;

            foreach (var providerId in providers)
            {
                var provider = graph.Find(providerId)!;
                var declared = provider.Objective(objective);

                if (declared is null)
                {
                    Add(ContinuityThreatReason.ProviderObjectiveAbsent, objective, ContinuityThreatClass.NotVerified, providerId);
                    continue;
                }

                if (declared > own)
                    Add(ContinuityThreatReason.ProviderExceedsRequirement, objective, ContinuityThreatClass.Confirmed, providerId);

                switch (statusOf(providerId, objective))
                {
                    case ObjectiveVerificationStatus.NotMet:
                        Add(ContinuityThreatReason.ProviderNotMet, objective, ContinuityThreatClass.Confirmed, providerId);
                        break;
                    case ObjectiveVerificationStatus.Unverified:
                        Add(ContinuityThreatReason.ProviderUnverified, objective, ContinuityThreatClass.NotVerified, providerId);
                        break;
                }
            }
        }

        dto.Items = items.Values
            .OrderByDescending(i => i.Weight)
            .ThenBy(i => i.Objective)
            .ThenBy(i => i.Reason)
            .ThenBy(i => i.ViaEntityId ?? 0)
            .ToList();

        dto.ThreatWeight = dto.Items.Count == 0 ? 0m : dto.Items.Max(i => i.Weight);
        dto.IsThreatened = dto.ThreatWeight > 0m;

        return dto;
    }
}
