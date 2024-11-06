using System;
using System.Globalization;
using System.Linq;
using Avalonia.Data;
using Avalonia.Data.Converters;
using ClientServices.Interfaces;
using DAL.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace GUIClient.Converters;

public class EntityToStringConverter: BaseConverter, IValueConverter
{
    public static readonly EntityToStringConverter Instance = new();
    
    public object? Convert( object? value, 
        Type targetType, 
        object? parameter, 
        CultureInfo culture)
    {
        if (value is null) return "";
        
        var memoryCacheService = GetService<IMemoryCacheService>();
        
        if (value is Entity entityData && targetType.IsAssignableTo(typeof(string)))
        {
            var entityName = entityData.EntitiesProperties.Where(ep => ep.Type == "name").Select(ep => ep.Value).FirstOrDefault();

            if (entityName == null) return "";
            
            memoryCacheService.Set<Entity>(entityName,entityData,TimeSpan.FromMinutes(20));

            return entityName;
        }
        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }
    
    public object? ConvertBack( object? value, Type targetType, object? parameter, CultureInfo culture )
    {
        var memoryCacheService = GetService<IMemoryCacheService>();
        if (value is string entityName && targetType.IsAssignableTo(typeof(Entity)))
        {
            return memoryCacheService.Get<Entity>(entityName);
        }

        throw new NotSupportedException();
    }
}