namespace GUIClient.Models;

public enum AvaliableViews
{
    Dashboard,
    MasterDashboard,
    Devices,
    Assessment,
    Risk,
    Users,
    Entities,
    Vulnerabilities,
    Incidents,

    /// <summary>The Cyber Risk Overview (S40, M54). Appended last: the values above are never renumbered.</summary>
    RiskOverview
}