using Avalonia;
using System;
using System.Globalization;
using Avalonia.Data.Converters;


namespace GUIClient.Converters;

public class PercentageConverter : IValueConverter
{
    public double Factor { get; set; } = 0.5; // Default to 50%

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if(value is int intValue)
        {
            var factor = parameter != null ? System.Convert.ToDouble(parameter) : Factor;
            return intValue * factor;
        }
        if(value is float floatValue)
        {
            var factor = parameter != null ? System.Convert.ToDouble(parameter) : Factor;
            return floatValue * factor;
        }
        if (value is double number)
        {
            var factor = parameter != null ? System.Convert.ToDouble(parameter) : Factor;
            return number * factor;
        }

        return AvaloniaProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
