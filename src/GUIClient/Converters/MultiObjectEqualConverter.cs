using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Microsoft.Extensions.Localization;

namespace GUIClient.Converters;

public class MultiObjectEqualConverter: IMultiValueConverter
{
    public static readonly MultiObjectEqualConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count == 0)
        {
            return false;
        }
        
        var firstType = values[0]?.GetType();
        for (var i = 1; i < values.Count; i++)
        {
            if (values[i]?.GetType() != firstType)
            {
                return false;
            }
        }

        if (targetType.IsAssignableTo(typeof(bool)))
        {

            var firstObj = values[0];
            for (var i = 1; i < values.Count; i++)
            {
                if(!values[i]!.Equals(firstObj))
                {
                    return false;
                }
            }
            
            return true;
            
        }
        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }
    
 
}