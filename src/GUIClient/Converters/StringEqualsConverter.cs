using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace GUIClient.Converters;

/// <summary>
/// Returns <c>true</c> when the bound string equals the converter parameter
/// (case-insensitive). Used e.g. to enable the column list only for "Table" sections.
/// </summary>
public class StringEqualsConverter : IValueConverter
{
    public static readonly StringEqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && parameter is string p &&
           string.Equals(s, p, StringComparison.OrdinalIgnoreCase);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
