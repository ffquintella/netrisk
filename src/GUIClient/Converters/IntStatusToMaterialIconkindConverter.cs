using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using GUIClient.Tools;

namespace GUIClient.Converters;

public class IntStatusToMaterialIconkindConverter: IValueConverter
{
    
    public static readonly IntStatusToMaterialIconkindConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);

        // The table and the target-type rule live in Tools/StatusIconMap, which GUIClient.Tests covers.
        return StatusIconMap.TryResolve(value, targetType, out var kind)
            ? kind
            : new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}