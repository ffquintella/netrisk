using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Microsoft.Extensions.Localization;
using Model.Authentication;

namespace GUIClient.Converters;

public class AuthenticationMethodsContainConverter: IValueConverter
{
    public static readonly AuthenticationMethodsContainConverter Instance = new();

    public object? Convert(
                            object? value, 
                            Type targetType, 
                            object? parameter, 
                            CultureInfo culture)
    {
        if (value is List<AuthenticationMethod> authentications && parameter is string compareText
                                       && targetType.IsAssignableTo(typeof(bool)))
        {

            foreach (var authentication in authentications)
            {
                if (authentication.Name == compareText)
                {
                    return true;
                }
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