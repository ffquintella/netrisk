using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;


namespace GUIClient.Converters;

public class IntEqualConverter: IValueConverter
{
    public static readonly IntEqualConverter Instance = new();

    public object? Convert(
                            object? value, 
                            Type targetType, 
                            object? parameter, 
                            CultureInfo culture)
    {
        if (value is int sourceValue && parameter is int compareValue
                                       && targetType.IsAssignableTo(typeof(bool)))
        {

            if (sourceValue == compareValue)
            {
                return true;
            }
            return false;
            
        }
        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }
    
    public object ConvertBack( object? value, Type targetType, object? parameter, CultureInfo culture )
    {
        throw new NotSupportedException();
    }

}