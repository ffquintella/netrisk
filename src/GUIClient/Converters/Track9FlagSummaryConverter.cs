using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
namespace GUIClient.Converters;
public sealed class Track9FlagSummaryConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 5 || values[0] is not int id) return string.Empty;
        if (values[2] is not true) return values[4] as string ?? string.Empty;
        return values[1] is IReadOnlyDictionary<int, string> rows && rows.TryGetValue(id, out var text)
            ? text : values[3] as string ?? string.Empty;
    }
}
