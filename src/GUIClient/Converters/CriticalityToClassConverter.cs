using System;
using System.Globalization;
using Avalonia.Data.Converters;
using GUIClient.Tools.Hosts;

namespace GUIClient.Converters;

/// <summary>
/// Drives the modifier class of a <c>Border.criticality</c> pill from a host's criticality.
///
/// Used the way the repository already toggles a class from data (<c>Border.toast</c>,
/// <c>Border.gantt_bar</c>): one <c>Classes.c1="{Binding …}"</c> per modifier, each asking "is it
/// this one?" with the class name as the parameter. Without a parameter it returns the class name
/// itself. The mapping is <see cref="CriticalityScale.ClassFor"/>.
/// </summary>
public class CriticalityToClassConverter : IValueConverter
{
    public static readonly CriticalityToClassConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var cssClass = CriticalityScale.ClassFor(value as int?);

        return parameter is string wanted ? string.Equals(cssClass, wanted, StringComparison.Ordinal) : cssClass;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
