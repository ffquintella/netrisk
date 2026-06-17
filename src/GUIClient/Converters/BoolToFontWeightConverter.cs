using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace GUIClient.Converters;

/// <summary>Returns a bold font weight when the bound boolean is true, otherwise normal.</summary>
public class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? FontWeight.Bold : FontWeight.Normal;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
