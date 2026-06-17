using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace GUIClient.Converters;

/// <summary>Returns a left indent when the bound boolean is true (used to nest sub-questions).</summary>
public class BoolToIndentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? new Thickness(24, 0, 0, 0) : new Thickness(0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
