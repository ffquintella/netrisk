using System;
using System.Globalization;
using Avalonia.Data.Converters;
using GUIClient.Tools.Hosts;

namespace GUIClient.Converters;

/// <summary>
/// Drives the modifier class of a <c>Border.severity</c> pill from a finding's severity
/// (<c>"0"</c>–<c>"4"</c>, or the numeric rank). Same contract as
/// <see cref="CriticalityToClassConverter"/>: with a class name as parameter it answers whether that
/// class applies. The mapping is <see cref="SeverityScale.ClassFor"/>.
/// </summary>
public class SeverityToClassConverter : IValueConverter
{
    public static readonly SeverityToClassConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var severity = value switch
        {
            int rank => rank.ToString(CultureInfo.InvariantCulture),
            _ => value as string
        };

        var cssClass = SeverityScale.ClassFor(severity);

        return parameter is string wanted ? string.Equals(cssClass, wanted, StringComparison.Ordinal) : cssClass;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
