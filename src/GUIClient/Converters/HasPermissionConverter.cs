using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using DAL.Entities;

namespace GUIClient.Converters;

public class HasPermissionConverter: IValueConverter
{
    public static readonly HasPermissionConverter Instance = new();

    public object? Convert(
        object? value, 
        Type targetType, 
        object? parameter, 
        CultureInfo culture)
    {
        if(value == null) throw new ArgumentNullException(nameof(value));
        
        if (value is List<Permission> permissions && parameter is string compareText
                                       && targetType.IsAssignableTo(typeof(bool)))
        {

            if(permissions.Count == 0) return false;
            
            var permission = permissions.Find(p => p.Key == compareText);
            
            return permission != null;
        }
        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }
    
    public object ConvertBack( object? value, Type targetType, object? parameter, CultureInfo culture )
    {
        throw new NotSupportedException();
    }
}