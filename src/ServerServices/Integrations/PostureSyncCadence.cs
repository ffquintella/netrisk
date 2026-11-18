namespace ServerServices.Integrations;

/// <summary>
/// When a scheduled posture sync (Vision One, SecurityScorecard) is due (M57, T276, S39 D-13). The old rule was "the last
/// run's end plus the interval", and with a daily job a run that ended at 03:20 was not due at 03:00 the next day, so a
/// 24-hour interval synced every other day. The due date now lives on a fixed grid of interval-long slots counted from the
/// Unix epoch (UTC): a run done in one slot makes the connection due at the start of the next, whatever time the run
/// started or ended, and a manual sync never moves it.
/// </summary>
public static class PostureSyncCadence
{
    public const int MinIntervalHours = 1;
    public const int MaxIntervalHours = 168;

    /// <summary>Due when it never ran on schedule (no due date yet) or the due date has come.</summary>
    public static bool IsDue(DateTime? nextDueAt, DateTime nowUtc) => nextDueAt is null || nextDueAt.Value <= nowUtc;

    /// <summary>The start of the slot after the one <paramref name="nowUtc"/> falls in, for an interval of <paramref name="intervalHours"/>.</summary>
    public static DateTime NextDue(DateTime nowUtc, int intervalHours)
    {
        var interval = TimeSpan.FromHours(Math.Clamp(intervalHours, MinIntervalHours, MaxIntervalHours)).Ticks;
        var slot = Math.Max(0L, (nowUtc - DateTime.UnixEpoch).Ticks) / interval;
        return DateTime.SpecifyKind(DateTime.UnixEpoch.AddTicks((slot + 1) * interval), DateTimeKind.Utc);
    }
}
