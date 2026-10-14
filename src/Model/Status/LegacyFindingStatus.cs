using System;
using System.Collections.Generic;
using DAL.Enums;

namespace Model.Status;

/// <summary>
/// The bridge between a finding's two status columns (S39 D-04, T239): the register's legacy
/// workflow column <c>vulnerabilities.Status</c> (<see cref="IntStatus"/>, what the desktop status
/// buttons drive) and the finding lifecycle <c>vulnerabilities.status_id</c>
/// (<see cref="FindingStatus"/>, what the API, the SLA and the risk index read).
///
/// The desktop used to close and reject findings by writing only the legacy column, so a finding an
/// analyst closed still counted as open everywhere the lifecycle is read. One table now says which
/// lifecycle state each closing legacy value means. Three readers depend on it agreeing with each
/// other, and the tests pin all three: the desktop close/reject path
/// (<c>IVulnerabilitiesService.CloseThroughLifecycleAsync</c>), the one-off backfill of
/// <c>status_id</c> in the numbered SQL (<c>ConsoleClient.Tests</c> parses its CASE against this
/// table), and <see cref="ClosedStatuses"/> (whose set is exactly this table's keys).
///
/// The mapping extends the one <c>Structure/77.sql</c> seeded the lifecycle with — Closed, Mitigated,
/// Fixed, Solved and FixVerified are Mitigated; Rejected is FalsePositive; NotRelevant and
/// FixNotRequired are OutOfScope; Duplicated is Duplicate — with the values 77 left Active. Retired,
/// Deleted, Cancelled, Completed and Outdated go to Mitigated rather than to a suppressed state: a
/// scanner reporting one of those findings again means the asset or the defect is back, and Mitigated
/// is the one closed state a re-import reactivates. Suppressing them would hide a returning defect,
/// and an under-open register hides real work where an over-open one is only a nuisance.
/// </summary>
public static class LegacyFindingStatus
{
    /// <summary>Every legacy workflow value that closes a finding, and the lifecycle state it means.</summary>
    public static readonly IReadOnlyDictionary<int, FindingStatus> ClosingTargets = new Dictionary<int, FindingStatus>
    {
        [(int)IntStatus.Closed] = FindingStatus.Mitigated,
        [(int)IntStatus.Mitigated] = FindingStatus.Mitigated,
        [(int)IntStatus.Fixed] = FindingStatus.Mitigated,
        [(int)IntStatus.Solved] = FindingStatus.Mitigated,
        [(int)IntStatus.FixVerified] = FindingStatus.Mitigated,
        [(int)IntStatus.Completed] = FindingStatus.Mitigated,
        [(int)IntStatus.Retired] = FindingStatus.Mitigated,
        [(int)IntStatus.Deleted] = FindingStatus.Mitigated,
        [(int)IntStatus.Cancelled] = FindingStatus.Mitigated,
        [(int)IntStatus.Outdated] = FindingStatus.Mitigated,
        [(int)IntStatus.Rejected] = FindingStatus.FalsePositive,
        [(int)IntStatus.NotRelevant] = FindingStatus.OutOfScope,
        [(int)IntStatus.FixNotRequired] = FindingStatus.OutOfScope,
        [(int)IntStatus.Duplicated] = FindingStatus.Duplicate
    };

    /// <summary>The lifecycle state a closing legacy value means, or null when the value does not close.</summary>
    public static FindingStatus? ClosingTarget(int legacyStatus) =>
        ClosingTargets.TryGetValue(legacyStatus, out var target) ? target : null;

    /// <summary>
    /// The lifecycle transition a desktop close or reject with <paramref name="finalStatus"/> needs,
    /// given the finding's lifecycle state, or null when the lifecycle must not move.
    ///
    /// A finding the lifecycle still holds open (Active, Verified) moves to the target. So does a
    /// risk-accepted one, deliberately: the server refuses RiskAccepted → Mitigated, and the operator
    /// is told to revoke the acceptance first instead of the close quietly skipping the lifecycle — a
    /// skipped move would leave the finding counted as exposed while the desktop shows it closed.
    /// A finding the lifecycle already holds closed (Mitigated, FalsePositive, OutOfScope, Duplicate)
    /// keeps that verdict: the desktop close only relabels the legacy column, and both columns already
    /// agree that the finding is not open.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="finalStatus"/> does not close a finding.</exception>
    public static FindingStatus? TransitionForClose(int finalStatus, FindingStatus current)
    {
        var target = ClosingTarget(finalStatus)
                     ?? throw new ArgumentOutOfRangeException(nameof(finalStatus), finalStatus,
                         "Not a closing workflow status; closing with it would leave the lifecycle open.");

        if (current == target) return null;

        return current.IsOpen() || current == FindingStatus.RiskAccepted ? target : null;
    }

    /// <summary>
    /// The lifecycle transition a desktop reopen needs: back to Active from any closed state, which
    /// the state machine allows from every one of them. An open finding needs none, and a
    /// risk-accepted one is left to its acceptance — reopening it here would bypass the revocation
    /// that records why the acceptance ended.
    /// </summary>
    public static FindingStatus? TransitionForReopen(FindingStatus current) =>
        current is FindingStatus.Mitigated or FindingStatus.FalsePositive or FindingStatus.OutOfScope
            or FindingStatus.Duplicate
            ? FindingStatus.Active
            : null;
}
