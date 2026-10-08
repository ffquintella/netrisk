using System.Collections.Generic;
using DAL.Enums;
using Model.RiskFlags;
namespace GUIClient.Tools.Track9;
public static class Track9RegisterFlags
{
    public static bool Matches(int riskId, RiskFlagCode? filter, IReadOnlyDictionary<int, FlaggedRiskDto>? rows) =>
        filter is null || (rows is not null && rows.TryGetValue(riskId, out var row) && row.Flags.Contains(filter.Value));
}
