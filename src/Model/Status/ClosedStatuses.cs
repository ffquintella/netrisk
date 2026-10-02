using System.Collections.Generic;

namespace Model.Status;

/// <summary>
/// The <see cref="IntStatus"/> values that take a vulnerability or an incident out of the open
/// population. Everything not listed counts as open, so a status added later shows up as work
/// outstanding rather than silently vanishing.
///
/// One definition for every count of "open" findings: the Master Dashboard rollup and the per-host
/// severity summary (S38 §5.3) both read this set, so the dashboard and the Hosts header cannot
/// disagree about the same finding. It was a private field of <c>MasterDashboardService</c> until
/// the host summary needed the same answer.
/// </summary>
public static class ClosedStatuses
{
    /// <summary>The closed set, as the integer values stored in <c>status</c> columns.</summary>
    public static readonly IReadOnlySet<int> Values = new HashSet<int>
    {
        (int)IntStatus.Closed,
        (int)IntStatus.NotRelevant,
        (int)IntStatus.Rejected,
        (int)IntStatus.Duplicated,
        (int)IntStatus.Fixed,
        (int)IntStatus.Solved,
        (int)IntStatus.Retired,
        (int)IntStatus.Deleted,
        (int)IntStatus.Completed,
        (int)IntStatus.Cancelled
    };

    /// <summary>True when <paramref name="status"/> is in the closed set.</summary>
    public static bool IsClosed(int status) => Values.Contains(status);

    /// <summary>True when <paramref name="status"/> still counts as open work.</summary>
    public static bool IsOpen(int status) => !Values.Contains(status);
}
