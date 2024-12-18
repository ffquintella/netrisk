using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Model.Authentication;

namespace GUIClient.Converters;

public class AuthenticatedUserHasPermissionConverter: IValueConverter
{
    public static readonly AuthenticatedUserHasPermissionConverter Instance = new();

    public object? Convert(
        object? value, 
        Type targetType, 
        object? parameter, 
        CultureInfo culture)
    {
        if (value == null) return false;
        
        if (value is AuthenticatedUserInfo uinfo && parameter is string compareText
                                                        && targetType.IsAssignableTo(typeof(bool)))
        {

            if (uinfo.IsAdmin) return true;
            
            if(uinfo.UserPermissions == null) return false;
            
            var permissions = uinfo.UserPermissions.ToList();
            
            if(permissions.Count == 0) return false;
            
            var permission = permissions.Find(p => p == compareText);
            
            return permission != null;
        }

        return false;
    }
    
    public object ConvertBack( object? value, Type targetType, object? parameter, CultureInfo culture )
    {
        throw new NotSupportedException();
    }
}