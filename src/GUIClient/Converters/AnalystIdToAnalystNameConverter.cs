using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using ClientServices.Interfaces;
using GUIClient.Exceptions;
using Model.Exceptions;
using Splat;

namespace GUIClient.Converters;

public class AnalystIdToAnalystNameConverter: NotReturnConverter, IValueConverter
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
                
                if (parameter != null && parameter is string variation)
                {
                    if(variation == "keepId") return $"{user} ({sourceId})";
                }
                
                return user;
            }
            catch 
            {
                return "";
            }

        }
        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }
    
}