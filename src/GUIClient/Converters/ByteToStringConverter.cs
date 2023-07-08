using System;
using System.Globalization;
using System.Text;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Microsoft.Extensions.Localization;

namespace GUIClient.Converters;

public class ByteToStringConverter: IValueConverter
{
    public static readonly ByteToStringConverter Instance = new();

    public object? Convert( object? value, 
                            Type targetType, 
                            object? parameter, 
                            CultureInfo culture)
    {
        if (value is null) return "";
        
        if (value is byte[] sourceData && targetType.IsAssignableTo(typeof(string)))
        {
            if (sourceData.Length == 0) return " ";

            return System.Text.Encoding.UTF8.GetString(sourceData);
            
        }
        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }
    
    public object ConvertBack( object? value, Type targetType, object? parameter, CultureInfo culture )
    {
        if (value is string sourceData && targetType.IsAssignableTo(typeof(byte[])))
        {
            if (sourceData.Length == 0) return " ";

            return Encoding.UTF8.GetBytes(sourceData);

        }
        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
        

    }

}