using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using ClientServices.Interfaces;
using GUIClient.Exceptions;
using Model.Exceptions;
using Splat;

namespace GUIClient.Converters;

public class AnalystIdToAnalystNameConverter: IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) throw new InvalidParameterException("analystId","Invalid null analyst id to convert");
        
        if (value is int sourceId && targetType.IsAssignableTo(typeof(string)))
        {
            var usersService = GetService<IUsersService>();

            try
            {
                var user = usersService.GetUserName(sourceId);
                return user;
            }
            catch (Exception e)
            {
                return "";
            }

        }
        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
    
    protected static T GetService<T>()
    {
        var result = Locator.Current.GetService<T>();
        if (result == null) throw new Exception("Could not find service of class: " + typeof(T).Name);
        return result;
    } 
}