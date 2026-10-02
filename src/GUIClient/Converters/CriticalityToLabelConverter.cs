using System;
using System.Globalization;
using Avalonia.Data.Converters;
using GUIClient.Tools.Hosts;

namespace GUIClient.Converters;

/// <summary>
/// A host's criticality (<c>int?</c>, 1–5) as text (S38 §3.5). No parameter: the localized name
/// (<c>Critical</c>, <c>Not set</c>). <c>long</c>: name and level (<c>Critical 5</c>), the header
/// pill. <c>short</c>: the digit or a dash, the host-list pill. The mapping is
/// <see cref="CriticalityScale"/>, which is what <c>GUIClient.Tests</c> covers.
/// </summary>
public class CriticalityToLabelConverter : BaseConverter, IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var level = value as int?;

        return (parameter as string) switch
        {
            "short" => CriticalityScale.ShortLabel(level),
            "long" => CriticalityScale.LongLabel(level, Localizer[CriticalityScale.LabelKey(level)]),
            _ => Localizer[CriticalityScale.LabelKey(level)].Value
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
