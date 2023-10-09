using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using GUIClient.Extensions;
using Model;

namespace GUIClient.Converters;

public class IntStatusToStringConverter: IValueConverter
{
    public static readonly IntStatusToStringConverter Instance = new();
    
    public object? Convert( object? value, 
        Type targetType, 
        object? parameter, 
        CultureInfo culture)
    {
        if (value is null) return "";

        int intVal = 0;
        if(value is short) intVal = Int32.Parse(value!.ToString()!);
        else if (value is ushort) intVal = Int32.Parse(value!.ToString()!);
        else if (value is int) intVal = Int32.Parse(value!.ToString()!);
        else if (value is long) intVal = Int32.Parse(value!.ToString()!);
        else throw new InvalidCastException();
        
        if (targetType.IsAssignableTo(typeof(string)))
        {
            var intEnum = (IntStatus) Enum.ToObject(typeof(IntStatus), intVal);
            return intEnum.StatusString();
            
        }
        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }
    
    public object ConvertBack( object? value, Type targetType, object? parameter, CultureInfo culture )
    {
        throw new NotSupportedException();
    }
}