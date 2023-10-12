using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using ClientServices.Interfaces;

namespace GUIClient.Converters;

public class UserIdToUserNameConverter: BaseConverter, IValueConverter
{
    public static readonly StringIdToImpactConverter Instance = new();
    
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    { 
        if (value is null ) return "";
        
        var usersService = GetService<IUsersService>();

        if (value is int id)
        {
            var userName = usersService.GetUserName(id);
            
            return userName;    
        }
        //return false;
        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}