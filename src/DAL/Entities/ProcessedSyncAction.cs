namespace DAL.Entities;

/// <summary>
/// Idempotency ledger for website-originated sync actions. Each visitor action carries a
/// stable client action id; recording it here lets the server safely re-apply (skip) actions
/// that arrive more than once under at-least-once delivery.
/// </summary>
public partial class ProcessedSyncAction
{
    public string ClientActionId { get; set; } = null!;

    public string ActionType { get; set; } = null!;

    public DateTime AppliedAt { get; set; }
}
