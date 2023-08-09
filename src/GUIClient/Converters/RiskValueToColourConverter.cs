using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace GUIClient.Converters;

public class RiskValueToColourConverter: IValueConverter
{
    public static readonly RiskValueToColourConverter Instance = new();
    
    public object? Convert( object? value, 
        Type targetType, 
        object? parameter, 
        CultureInfo culture)
    {
        if (value is null) return null;
        
        if (value is float sourceData && targetType.IsAssignableTo(typeof(Avalonia.Media.IBrush)))
        {

            switch (sourceData)
            {
                case < 2:
                    return new SolidColorBrush(Colors.LightGreen);
                case >= 2 and < 4:
                    return new SolidColorBrush(Colors.Turquoise);
                case >= 4 and < 6:
                    return new SolidColorBrush(Colors.LightSalmon);
                case >= 6 and < 8:
                    return new SolidColorBrush(Colors.Orange);
                case >= 8:
                    return new SolidColorBrush(Colors.OrangeRed);
                default:
                    return new SolidColorBrush(Colors.Turquoise);
            }
        }
        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }
    
    public object ConvertBack( object? value, Type targetType, object? parameter, CultureInfo culture )
    {
        throw new NotSupportedException();
    }
}