namespace DAL.Enums;

/// <summary>
/// Lifecycle of a formal risk acceptance (Track 3 milestone 3.2.3, entity design generalized by
/// Track 8.1).
/// </summary>
public enum RiskAcceptanceStatus
{
    /// <summary>In force. The findings it covers are suppressed.</summary>
    Active = 1,

    /// <summary>Past its expiry date. The expiry job reactivates whatever it covered.</summary>
    Expired = 2,

    /// <summary>Withdrawn before expiry by a human decision.</summary>
    Revoked = 3
}
