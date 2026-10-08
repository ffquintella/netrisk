using System;

namespace Tools.DecisionCycle;

/// <summary>Where a committee decision stands after a vote (S50 §4.6).</summary>
public enum CommitteeTallyOutcome
{
    /// <summary>Neither reached nor out of reach: voting goes on.</summary>
    Pending,

    /// <summary><c>required</c> distinct members approved.</summary>
    Approved,

    /// <summary>Even if every member who has not voted approved, the required approvals would not be reached.</summary>
    Rejected
}

/// <summary>
/// The collegiate rule of a risk committee (S50 §4.6, D9): <em>k</em> approvals of the eligible members. A decision is
/// approved the moment the <em>k</em>-th approval is cast, and rejected the moment it can no longer get there — a reject
/// and an abstention both count against it, because neither is an approval. Pure, so it is tested alone.
/// </summary>
public static class CommitteeTally
{
    /// <param name="requiredApprovals">The committee's <em>k</em>, at least two.</param>
    /// <param name="eligibleVoters">
    /// The members who may vote on this decision — not the risk's submitter, owner or manager, not the third line —
    /// plus any member who already voted and has since left the committee: a vote cast stays cast.
    /// </param>
    public static CommitteeTallyOutcome Evaluate(int requiredApprovals, int eligibleVoters, int approvals,
        int rejections, int abstentions)
    {
        if (requiredApprovals < 1) throw new ArgumentOutOfRangeException(nameof(requiredApprovals));
        if (eligibleVoters < 0 || approvals < 0 || rejections < 0 || abstentions < 0)
            throw new ArgumentOutOfRangeException(nameof(eligibleVoters), "Counts are never negative.");

        if (approvals >= requiredApprovals) return CommitteeTallyOutcome.Approved;

        var outstanding = System.Math.Max(0, eligibleVoters - (approvals + rejections + abstentions));

        return approvals + outstanding < requiredApprovals
            ? CommitteeTallyOutcome.Rejected
            : CommitteeTallyOutcome.Pending;
    }

    /// <summary>Whether a decision can be opened at all: there must be at least <c>k</c> eligible members.</summary>
    public static bool Reachable(int requiredApprovals, int eligibleVoters) => eligibleVoters >= requiredApprovals;
}
