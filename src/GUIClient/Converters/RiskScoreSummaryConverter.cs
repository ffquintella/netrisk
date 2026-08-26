using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using GUIClient.Tools;
using Model.Governance;

namespace GUIClient.Converters;

/// <summary>
/// Renders the inherent → residual summary for one row of the risk register (Track 8 milestone
/// 8.2.3), including the business rank a review campaign assigned (8.6.5).
///
/// A multi-value converter rather than a property on the bound item, because the list is bound to
/// <c>DAL.Entities.Risk</c> and the residual score lives on <c>risk_scoring</c>. The alternative was
/// wrapping every risk in a list-item view-model, which would have meant touching every one of the
/// thirty places <c>RiskViewModel</c> reads <c>Risks</c> — a large, untestable change to a window
/// that cannot be launched in this environment.
///
/// Inputs, in order: the risk id, the view-model's score lookup, and the business rank.
/// </summary>
public class RiskScoreSummaryConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[0] is not int riskId) return string.Empty;

        // Bindings resolve out of order while the list virtualises, so an unresolved lookup is normal
        // and means "not loaded yet", not "no scores".
        if (values[1] is not IReadOnlyDictionary<int, RiskScorePair> scores) return string.Empty;

        var rank = values.Count > 2 && values[2] is int businessRank ? businessRank : (int?)null;

        if (!scores.TryGetValue(riskId, out var pair))
            return RiskScoreSummary.Describe(null, null, rank, culture);

        return RiskScoreSummary.Describe(pair.Inherent, pair.Residual, rank, culture);
    }
}
