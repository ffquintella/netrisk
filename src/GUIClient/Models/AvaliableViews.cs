namespace GUIClient.Models;

/// <summary>
/// The shell's modules. Values are pinned and never renumbered: 1 was the Master Dashboard, retired by T294, and is
/// not reused.
/// </summary>
public enum AvaliableViews
{
    Dashboard = 0,
    Devices = 2,
    Assessment = 3,
    Risk = 4,
    Users = 5,
    Entities = 6,
    Vulnerabilities = 7,
    Incidents = 8,

    /// <summary>The Cyber Risk Overview (S40, M54). Appended last: the values above are never renumbered.</summary>
    RiskOverview = 9
}
