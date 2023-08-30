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

public class EntityListToStringListConverter: BaseConverter, IValueConverter
{

    public EntityListToStringListConverter() : base()
    {
        
    }
    
    public static readonly EntityListToStringListConverter Instance = new();
    
    public object? Convert( object? value, 
        Type targetType, 
        object? parameter, 
        CultureInfo culture)
    {
        var result = new List<string>();
        if (value is null) return result;
        
        if (value is ObservableCollection<Entity> sourceData && targetType.IsAssignableTo(typeof(IEnumerable)))
        {
            foreach (var entity in sourceData)
            {
                var valueProperty = entity.EntitiesProperties.Where(ep => ep.Type == "name").Select(ep => ep.Value).FirstOrDefault();
                
                if(valueProperty is null) throw new InvalidCastException("Could not cast entity to string");
                
                //var valueString = Localizer[valueProperty];
                
                result.Add(valueProperty);
            }

            return result;

        }
        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }
    
    public object ConvertBack( object? value, Type targetType, object? parameter, CultureInfo culture )
    {
        throw new NotSupportedException();
    }
}