using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using GUIClient.Exceptions;
using Material.Icons;

namespace GUIClient.Converters;

public class TypeToIconConverter: IValueConverter
{
    public static readonly TypeToIconConverter Instance = new();

    public object? Convert(object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        if (value is null) return "";
        
        if (value is string sourceData && targetType.IsAssignableTo(typeof(MaterialIconKind)))
        {
            if (sourceData.Length == 0) throw new InvalidStatusException("Invalid empty Type to convert", sourceData);

            switch (sourceData)
            {
                case "application/pdf":
                    return MaterialIconKind.FilePdfBox;
                case "text/plain":
                    return MaterialIconKind.Text;
                default:
                    return MaterialIconKind.Null;

            }
        }

        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }
    
    public object ConvertBack( object? value, Type targetType, object? parameter, CultureInfo culture )
    {
        throw new NotSupportedException();
    }
}