using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Model.Exceptions;

namespace GUIClient.Converters;

public class DateToFormatedStringConverter: BaseConverter, IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return AvaloniaProperty.UnsetValue;
            //throw new InvalidParameterException("date","Invalid null date id to convert");
        
        if (value is DateTime sourceDate && targetType.IsAssignableTo(typeof(string)))
        {
            if (parameter != null && parameter is string format)
            {
                return sourceDate.ToString(format);
            }
            if(culture.Name == "en-US")
            {
                return sourceDate.ToString("MM/dd/yyyy");
            }
            if(culture.Name == "pt-BR")
            {
                return sourceDate.ToString("dd/MM/yyyy");
            }
            return sourceDate.ToString("yyyy-MM-dd");
        }
        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}