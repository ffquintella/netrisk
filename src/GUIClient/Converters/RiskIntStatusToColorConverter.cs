using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GUIClient.Exceptions;

namespace GUIClient.Converters;

public class RiskIntStatusToColorConverter: IValueConverter
{
    public static readonly RiskIntStatusToColorConverter Instance = new();
    
    public object? Convert( object? value, 
        Type targetType, 
        object? parameter, 
        CultureInfo culture)
    {
        if (value is null) return "";
        
        if (value is int sourceData && targetType.IsAssignableTo(typeof(Avalonia.Media.IBrush)))
        {
            if (sourceData < 0) throw new InvalidStatusException("Invalid int value to convert", "sourceData");

            switch (sourceData)
            {
                
                case 0:
                    return new SolidColorBrush(Colors.White); 
                case 1:
                    return new SolidColorBrush(Colors.LightGreen);
                case 2:
                    return new SolidColorBrush(new Color(255, 144, 238, 144));
                case 3:
                    return new SolidColorBrush(Colors.Orange);
                case 4:
                    return new SolidColorBrush(Colors.LightSalmon);
                case 5:
                    return new SolidColorBrush(Colors.Red);
                default:
                    return new SolidColorBrush(Colors.DarkRed);
                    //return new SolidColorBrush(Colors.Turquoise);

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