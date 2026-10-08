using System;
using System.Collections.Generic;

namespace GUIClient.Tools.Track9;

/// <summary>Small input rules shared by the typed Track 9 forms.</summary>
public static class Track9MonitoringInputRules
{
    public static bool CanAssessBacktest(IEnumerable<int> riskIds, bool noCorrespondingScenario)
    {
        ArgumentNullException.ThrowIfNull(riskIds);
        var anyRisk = false;
        foreach (var id in riskIds)
        {
            if (id <= 0) return false;
            anyRisk = true;
        }

        return anyRisk != noCorrespondingScenario;
    }

    public static List<int> PositiveDistinctIds(IEnumerable<int> riskIds)
    {
        ArgumentNullException.ThrowIfNull(riskIds);
        var seen = new HashSet<int>();
        var result = new List<int>();
        foreach (var id in riskIds)
            if (id > 0 && seen.Add(id))
                result.Add(id);
        return result;
    }
}
