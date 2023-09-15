using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Fizzler;


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
        if (value is null || parameter == null) return false;
        
        if (value is int sourceValue && parameter is string compareValue
                                       && targetType.IsAssignableTo(typeof(bool)))
        {

            var intCompare = Int32.Parse(compareValue);
            
            if (sourceValue == intCompare)
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