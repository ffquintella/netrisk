using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace GUIClient.Converters;

/// <summary>
/// Renders a collection of strings as one comma-separated line.
///
/// Used where a list is a property of a row rather than a row of its own — a token's scopes, the
/// available dedup strategies. Binding the collection directly would print the type name.
/// </summary>
public class StringJoinConverter : IValueConverter
{
    public static readonly StringJoinConverter Instance = new();

    /// <summary><paramref name="parameter"/> overrides the separator when given.</summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return string.Empty;

        var separator = parameter as string ?? ", ";

        // A plain string is passed through: a binding that sometimes carries a list and sometimes a
        // single value should not render the string character by character.
        if (value is string single) return single;

        if (value is IEnumerable items)
            return string.Join(separator, items.Cast<object?>().Where(i => i != null).Select(i => i!.ToString()));

        return value.ToString() ?? string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
