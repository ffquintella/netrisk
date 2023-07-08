using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Microsoft.Extensions.Localization;

namespace GUIClient.Converters;

public class StringEqualConverter: IValueConverter
{
    public static readonly StringEqualConverter Instance = new();

    public object? Convert(
                            object? value, 
                            Type targetType, 
                            object? parameter, 
                            CultureInfo culture)
    {
        if (value is string sourceText && parameter is string compareText
                                       && targetType.IsAssignableTo(typeof(bool)))
        {

            if (sourceText == compareText)
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