using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace GUIClient.Converters;

public class UShortEqualConverter: IValueConverter
{


    public static readonly IntEqualConverter Instance = new();

    public object? Convert(
        object? value, 
        Type targetType, 
        object? parameter, 
        CultureInfo culture)
    {
        if (value is null || parameter == null) return false;

        if (value is ushort sourceValue && parameter is string compareValue
                                     && targetType.IsAssignableTo(typeof(bool)))
        {

            var usCompare = ushort.Parse(compareValue);
            
            if (sourceValue == usCompare)
            {
                return true;
            }
            return false;
            
        }
        return false;
        // converter used for the wrong type
        //return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }
    
    public object ConvertBack( object? value, Type targetType, object? parameter, CultureInfo culture )
    {
        throw new NotSupportedException();
    }

}
