using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Data;
using Avalonia.Data.Converters;
using DAL.Entities;
using Microsoft.Extensions.Localization;

namespace GUIClient.Converters;

public class EntityPropertyListToNameStringConverter: BaseConverter, IValueConverter
{

    public EntityPropertyListToNameStringConverter() : base()
    {
        
    }
    
    public static readonly EntityPropertyListToNameStringConverter Instance = new();
    
    public object? Convert( object? value, 
        Type targetType, 
        object? parameter, 
        CultureInfo culture)
    {

        if (value is null) return "";
        
        if (value is List<EntitiesProperty> properties && targetType.IsAssignableTo(typeof(string)))
        {

            var name = properties.Where(ep => ep.Type == "name").Select(ep => ep.Value).FirstOrDefault();
            
            if(name is null) throw new InvalidCastException("Could not cast property to string");
            
            return name;

        }
        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }
    
    public object ConvertBack( object? value, Type targetType, object? parameter, CultureInfo culture )
    {
        throw new NotSupportedException();
    }
}