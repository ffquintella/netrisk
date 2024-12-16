using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace GUIClient.Converters;

public class StringToLocalizedStringConverter: BaseConverter, IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        
        if(value is null) return "";
        
        if (value is string sourceValue)
        {
            return Localizer[sourceValue];
        }
        
        return new BindingNotification(new Exception(""), BindingErrorType.Error);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}