using System;
using System.Globalization;
using System.Linq;
using Avalonia.Data;
using Avalonia.Data.Converters;
using DAL.Entities;

namespace GUIClient.Converters;

public class EntityToStringConverter: IValueConverter
{
    public static readonly EntityToStringConverter Instance = new();
    
    public object? Convert( object? value, 
        Type targetType, 
        object? parameter, 
        CultureInfo culture)
    {
        if (value is null) return "";
        
        if (value is Entity sourceData && targetType.IsAssignableTo(typeof(string)))
        {
            return sourceData.EntitiesProperties.Where(ep => ep.Type == "name").Select(ep => ep.Value).FirstOrDefault();
        }
        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }
    
    public object ConvertBack( object? value, Type targetType, object? parameter, CultureInfo culture )
    {
        throw new NotSupportedException();
    }
}