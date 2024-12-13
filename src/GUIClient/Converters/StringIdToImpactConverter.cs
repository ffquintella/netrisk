using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using ClientServices.Interfaces;
using Model.Globalization;
using Tools.Helpers;

namespace GUIClient.Converters;

public class StringIdToImpactConverter: BaseConverter, IValueConverter
{
    public static readonly StringIdToImpactConverter Instance = new();

    public object? Convert(
        object? value, 
        Type targetType, 
        object? parameter, 
        CultureInfo culture)
    {
        if (value is null ) return "";

        var impactsService = GetService<IImpactsService>();

        if (value is string sourceValue)
        {
            var inputList = AsyncHelper.RunSync(impactsService.GetAllAsync);
            
            if(!Int32.TryParse(sourceValue, out var key))         
                return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
            
            var impact = inputList.Find(x => x.Key == key);

            if (impact != null && impact.LocalizedValue != null)
            {
                if (parameter != null && parameter is string variation)
                {
                    if(variation == "keepId") return $"{key} ({impact.LocalizedValue})";
                }
                return impact.LocalizedValue;
            }
            if (parameter != null && parameter is string variation2)
            {
                if(variation2 == "keepId") return $"{key} ({impact!.Value})";
            }
            return impact!.Value;

        }
        //return false;
        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }
    
    public object? ConvertBack( object? value, Type targetType, object? parameter, CultureInfo culture )
    {
        //return null;
        throw new NotSupportedException();
    }

}
