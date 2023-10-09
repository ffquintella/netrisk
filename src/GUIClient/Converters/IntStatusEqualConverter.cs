using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using DAL.Entities;

namespace GUIClient.Converters;

public class IntStatusEqualConverter: IValueConverter
{
    public static readonly IntStatusEqualConverter Instance = new();

    public object? Convert(
        object? value, 
        Type targetType, 
        object? parameter, 
        CultureInfo culture)
    {
        if (value == null) return false;
        
        if (value is ushort status && parameter is string compareText
                                       && targetType.IsAssignableTo(typeof(bool)))
        {

            if(status.ToString() == compareText) return true;
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