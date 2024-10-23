using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Data;
using Avalonia.Data.Converters;
using ClientServices.Interfaces;
using Model.Exceptions;

namespace GUIClient.Converters;

public class EntityIdToNameConverter: BaseConverter, IValueConverter
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
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string sourceText && targetType.IsAssignableTo(typeof(int?)))
        {
            var pattern = @"\((?<idx>.+)\)";
            
            // Instantiate the regular expression object.
            var r = new Regex(pattern, RegexOptions.IgnoreCase);

            // Match the regular expression pattern against a text string.
            var m = r.Match(sourceText);
            var idx = m.Groups["idx"].Value;
            return int.Parse(idx);
        }

        //return 1;
        throw new NotSupportedException();
    }
}