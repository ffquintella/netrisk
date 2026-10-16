namespace DAL.Enums;

/// <summary>
/// Which integration a sync-log row belongs to (Track 4), persisted in
/// <c>integration_sync_logs.integration</c>.
///
/// One log table for every integration rather than one per provider: the operator question is
/// "what ran, and did it work", and answering it from four tables with four shapes means four
/// screens that drift apart.
/// </summary>
public enum IntegrationKind
{
    /// <summary>Issue-tracker push/pull (4.2).</summary>
    IssueTracker = 1,

    /// <summary>Trend Micro Vision One (4.4).</summary>
    TrendMicroVisionOne = 2,

    /// <summary>SecurityScorecard (4.5).</summary>
    SecurityScorecard = 3,

    /// <summary>SCIM provisioning (4.3.2).</summary>
    Scim = 4,

    /// <summary>Jira Service Management request and SLA mirror (4.6).</summary>
    JiraServiceManagement = 5,

    /// <summary>Jira Assets register import (4.6).</summary>
    JiraAssets = 6,

    // 7 is Tenable (S40 §7.3, T265), reserved: the values are explicit and only ever appended.

    /// <summary>The Consolidated Risk Index's daily snapshot (M53, T247; S40 §9.3): one row per run, with its counts.</summary>
    RiskIndex = 8
}
