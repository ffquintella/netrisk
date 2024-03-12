using System;
using System.Globalization;
using System.Linq;
using Avalonia.Data;
using Avalonia.Data.Converters;
using ClientServices.Interfaces;
using Model.Exceptions;

namespace GUIClient.Converters;

public class EntityIdToNameConverter: NotReturnConverter, IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) throw new InvalidParameterException("entityId","Invalid null entity id to convert");
        
        if (value is int entityId && targetType.IsAssignableTo(typeof(string)))
        {
            var entitiesService = GetService<IEntitiesService>();

            try
            {
                var entity = entitiesService.GetEntity(entityId);

                //if (entity == null) return "";
                
                var name = entity.EntitiesProperties.FirstOrDefault(e => e.Type == "name")!.Value;
                
                if (parameter != null && parameter is string variation)
                {
                    if(variation == "keepId") return $"{name} ({entity.Id})";
                }
                
                return name;
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