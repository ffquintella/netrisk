using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Model.Exceptions;

namespace GUIClient.Converters;

public class TimeSpanToHoursStringConverter: BaseConverter, IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) throw new InvalidParameterException("date","Invalid null date id to convert");
        
        if (value is TimeSpan sourceTimeSpan && targetType.IsAssignableTo(typeof(string)))
        {
            if (parameter != null && parameter is string format)
            {
                return sourceTimeSpan.Hours.ToString(format);
            }
            return sourceTimeSpan.Hours.ToString();
        }
        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}