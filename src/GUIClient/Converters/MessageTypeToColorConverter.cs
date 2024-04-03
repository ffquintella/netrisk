using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GUIClient.Exceptions;
using Model.Messages;

namespace GUIClient.Converters;

public class MessageTypeToColorConverter: IValueConverter
{
    public static readonly MessageTypeToColorConverter Instance = new();
    
    public object? Convert( object? value, 
        Type targetType, 
        object? parameter, 
        CultureInfo culture)
    {
        if (value is null) return "";
        
        if (value is int sourceData && targetType.IsAssignableTo(typeof(Avalonia.Media.IBrush)))
        {
            if (sourceData < 0) throw new InvalidStatusException("Invalid int value to convert", "sourceData");

            switch (sourceData)
            {
                
                case (int)MessageType.Information:
                    return new SolidColorBrush(new Color(255, 59, 59, 59)); 
                case (int)MessageType.Warning:
                    return new SolidColorBrush(Colors.DarkOrange);
                case (int)MessageType.Error:
                    return new SolidColorBrush(Colors.DarkRed);
                default:
                    return new SolidColorBrush(Colors.Transparent);

            }
        }
        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }
    
    public object ConvertBack( object? value, Type targetType, object? parameter, CultureInfo culture )
    {
        throw new NotSupportedException();
    }
}