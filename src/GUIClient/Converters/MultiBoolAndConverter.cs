using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media.Immutable;

namespace GUIClient.Converters;


public sealed class MultiBoolAndConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if(values.Count == 0)
            return BindingOperations.DoNothing;
        
        // Ensure all bindings are provided and attached to correct target type
        if (values?.Count < 2 || !targetType.IsAssignableFrom(typeof(bool)))
            throw new NotSupportedException();

        // Ensure all bindings are correct type
        if (!values!.All(x => x is bool or UnsetValueType or null))
            throw new NotSupportedException();

        var boolValues = new List<bool>();
        
        foreach (var value in values!)
        {
            if (value is not bool) return BindingOperations.DoNothing;
            if(value is bool b)
                boolValues.Add(b);
        }
        
        return boolValues.All(x => x);
        
    }
}