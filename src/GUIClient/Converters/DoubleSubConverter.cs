using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace GUIClient.Converters;

public class DoubleSubConverter: IValueConverter
{
    public static readonly DoubleSubConverter Instance = new();

    public object? Convert(
                            object? value, 
                            Type targetType, 
                            object? parameter, 
                            CultureInfo culture)
    {
        if (value is double sourceValue && parameter is string subStrValue
                                       && targetType.IsAssignableTo(typeof(double)))
        {

            var subValue = double.Parse(subStrValue);
            
            return sourceValue - subValue;
        }
        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }
    
    public object ConvertBack( object? value, Type targetType, object? parameter, CultureInfo culture )
    {
        throw new NotSupportedException();
    }

}