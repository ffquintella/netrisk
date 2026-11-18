using ServerServices.Integrations.SecurityScorecard;
using ServerServices.Integrations.TrendMicro;

namespace ServerServices.Importers;

/// <summary>
/// Which finding imports count as a scan of the hosts they touch, and so stamp <c>hosts.last_assessed_at</c> (M58,
/// T281, S39 §8.2, D-18). Every scanner and code-scanner importer does, the Tenable feed included; the two feeds the
/// index reads on their own clocks do not: Vision One CVEs (the device's detection feed, W = 7 d from its own findings)
/// and SecurityScorecard issues (a pseudo-host for the external perimeter). This is the one list that names them.
/// </summary>
public static class HostAssessmentPolicy
{
    private static readonly HashSet<string> NotAssessing = new(StringComparer.OrdinalIgnoreCase)
    {
        TrendMicroService.ImporterName,
        SecurityScorecardService.ImporterName
    };

    /// <summary>Whether an import by <paramref name="importer"/> stamps the assessment date of its hosts.</summary>
    public static bool StampsLastAssessed(string? importer) =>
        !string.IsNullOrWhiteSpace(importer) && !NotAssessing.Contains(importer.Trim());
}
