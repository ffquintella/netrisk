using System;
using System.Globalization;

namespace GUIClient.Converters;

public class NoReturnConverter: BaseConverter
{
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}