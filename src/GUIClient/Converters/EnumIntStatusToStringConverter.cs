using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using GUIClient.Extensions;
using Model;

namespace GUIClient.Converters;

public class EnumIntStatusToStringConverter: IValueConverter
{
    public static readonly EnumIntStatusToStringConverter Instance = new();
    
    public object? Convert( object? value, 
        Type targetType, 
        object? parameter, 
        CultureInfo culture)
    {
        if (value is null) return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
        
        if (value is IntStatus valEnum && targetType.IsAssignableTo(typeof(string)))
        {
            return valEnum.StatusString();

        }
        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }
    
    public object ConvertBack( object? value, Type targetType, object? parameter, CultureInfo culture )
    {
        throw new NotSupportedException();
    }
}