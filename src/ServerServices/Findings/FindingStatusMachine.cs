using DAL.Enums;
using Model.Exceptions;

namespace ServerServices.Findings;

/// <summary>
/// The finding-lifecycle transition matrix (Track 3 milestone 3.2.1).
///
/// A pure function of (from, to, context), separate from the service that persists transitions, so
/// the rules are testable exhaustively without a database and so the same rules govern manual
/// triage, imports, and jobs. A state machine enforced only in the UI is not enforced.
/// </summary>
public static class FindingStatusMachine
{
    /// <summary>
    /// Which states each state may move to.
    ///
    /// The shape worth noting: every state can be re-opened to <see cref="FindingStatus.Active"/>.
    /// A wrong suppression has to be reversible, or the only way to correct a mistaken
    /// false-positive verdict is to edit the database. What is <em>not</em> permitted is skipping
    /// straight from a suppressed state to <see cref="FindingStatus.Mitigated"/>: claiming a finding
    /// was fixed while it was formally accepted skips the record of it ever coming back.
    /// </summary>
    private static readonly Dictionary<FindingStatus, FindingStatus[]> Allowed = new()
    {
        [FindingStatus.Active] =
        [
            FindingStatus.Verified, FindingStatus.FalsePositive, FindingStatus.OutOfScope,
            FindingStatus.Duplicate, FindingStatus.RiskAccepted, FindingStatus.Mitigated
        ],
        [FindingStatus.Verified] =
        [
            FindingStatus.Active, FindingStatus.FalsePositive, FindingStatus.OutOfScope,
            FindingStatus.Duplicate, FindingStatus.RiskAccepted, FindingStatus.Mitigated
        ],
        // Suppressed states go back to Active and nowhere else. Re-triage starts from open.
        [FindingStatus.FalsePositive] = [FindingStatus.Active],
        [FindingStatus.OutOfScope] = [FindingStatus.Active],
        [FindingStatus.RiskAccepted] = [FindingStatus.Active],
        [FindingStatus.Duplicate] = [FindingStatus.Active],
        // A mitigated finding a scanner reports again is a regression, which is a reactivation.
        // It may also be ruled a false positive: "we fixed it" and "it was never real" are both
        // things a later reviewer can conclude.
        [FindingStatus.Mitigated] = [FindingStatus.Active, FindingStatus.Verified, FindingStatus.FalsePositive]
    };

    public static IReadOnlyList<FindingStatus> AllowedTargets(FindingStatus from) =>
        Allowed.TryGetValue(from, out var targets) ? targets : [];

    public static bool CanTransition(FindingStatus from, FindingStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    /// <summary>
    /// Throws unless the transition is legal and carries everything the target state requires.
    ///
    /// <paramref name="justification"/> is mandatory for suppressing states and for
    /// <see cref="FindingStatus.Duplicate"/>; <paramref name="duplicateOfId"/> is mandatory for
    /// <see cref="FindingStatus.Duplicate"/>, because a duplicate that points at nothing is just a
    /// finding that has been made invisible.
    /// </summary>
    public static void Validate(FindingStatus from, FindingStatus to, string? justification, int? duplicateOfId,
        int? findingId = null)
    {
        if (from == to)
            throw new InvalidStateTransitionException(from.ToString(), to.ToString(),
                $"The finding is already {to}.");

        if (!CanTransition(from, to))
            throw new InvalidStateTransitionException(from.ToString(), to.ToString(),
                $"A finding cannot move from {from} to {to}. Allowed from {from}: " +
                $"{string.Join(", ", AllowedTargets(from))}.");

        if (to.RequiresJustification() && string.IsNullOrWhiteSpace(justification))
            throw new InvalidParameterException(nameof(justification),
                $"Moving a finding to {to} requires a stated reason.");

        if (to == FindingStatus.Duplicate)
        {
            if (duplicateOfId == null)
                throw new InvalidParameterException(nameof(duplicateOfId),
                    "Marking a finding as a duplicate requires the id of the canonical finding.");

            if (findingId != null && duplicateOfId == findingId)
                throw new InvalidParameterException(nameof(duplicateOfId),
                    "A finding cannot be a duplicate of itself.");
        }
    }

    /// <summary>
    /// What a re-import should do to a finding the scanner has reported again.
    ///
    /// This is the "sticky triage" rule (3.2.1) in one place: a suppressed verdict survives the
    /// scanner disagreeing with it, and a mitigated finding coming back is a regression worth
    /// re-opening and recording. Both behaviours are load-bearing — the first is what keeps a
    /// register usable, the second is what stops a reintroduced vulnerability from being invisible.
    /// </summary>
    public static ReimportOutcome OnSeenAgain(FindingStatus current) => current switch
    {
        // The scanner reporting it again tells us nothing new: the human verdict was about whether
        // the report was right, not whether it would recur.
        FindingStatus.FalsePositive => ReimportOutcome.KeepSuppressed,
        FindingStatus.OutOfScope => ReimportOutcome.KeepSuppressed,
        FindingStatus.RiskAccepted => ReimportOutcome.KeepSuppressed,

        // Someone decided this is the same defect as another finding; a fresh sighting does not
        // change that.
        FindingStatus.Duplicate => ReimportOutcome.KeepSuppressed,

        // We believed this was fixed and the scanner disagrees. That is a regression.
        FindingStatus.Mitigated => ReimportOutcome.Reactivate,

        // Already open: just record the sighting.
        _ => ReimportOutcome.Touch
    };
}

/// <summary>What a re-import does to an existing finding.</summary>
public enum ReimportOutcome
{
    /// <summary>Update last-seen and occurrence count; leave the status alone.</summary>
    Touch,

    /// <summary>As <see cref="Touch"/>, and explicitly do not reopen — the triage verdict stands.</summary>
    KeepSuppressed,

    /// <summary>Move back to Active and write a history event: the fix did not hold.</summary>
    Reactivate
}
