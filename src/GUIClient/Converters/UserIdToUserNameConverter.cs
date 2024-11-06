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
            //var userName = usersService.GetUserName(id);

            var userName = usersService.GetUserNameAsync(id).ConfigureAwait(false).GetAwaiter().GetResult();
            
            return userName;    
        }
        //return false;
        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var usersService = GetService<IUsersService>();
        var users = usersService.GetAllAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        
        if (value is string userName)
        {
            var user = users.Find(u => u.Name == userName);
            return user?.Id;
        }
        
        return 0;
        //throw new NotSupportedException();
    }
}