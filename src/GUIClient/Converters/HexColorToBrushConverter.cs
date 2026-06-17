using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace GUIClient.Converters;

/// <summary>
/// Converts a hex color string (e.g. <c>#2A3F54</c>) into an <see cref="IBrush"/> so the
/// template designer can show a live swatch next to the branding color inputs.
/// </summary>
public class HexColorToBrushConverter : IValueConverter
{
    public static readonly HexColorToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                return new SolidColorBrush(Color.Parse(hex));
            }
            catch
            {
                // Fall through to transparent on malformed input.
            }
        }
        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
