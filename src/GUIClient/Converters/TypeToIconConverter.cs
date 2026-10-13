using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using GUIClient.Exceptions;
using GUIClient.Tools;
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
        
        // MaterialIcon.Kind is a MaterialIconKind? — testing IsAssignableTo(typeof(MaterialIconKind)) refused it, so no
        // file-type icon was drawn on any screen that lists files (T333; the same rule T325 fixed for status icons).
        if (value is string sourceData && StatusIconMap.CanHoldIcon(targetType))
        {
            if (sourceData.Length == 0) throw new InvalidStatusException("Invalid empty Type to convert", sourceData);

            switch (sourceData)
            {
                case "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet":
                    return MaterialIconKind.FileExcel;
                case "text/word":
                    return MaterialIconKind.MicrosoftWord;
                case "application/msword":
                    return MaterialIconKind.MicrosoftWord;
                case "text/csv":
                    return MaterialIconKind.FileCsv;
                case "application/zip":
                    return MaterialIconKind.FolderZip;
                case "application/x-gzip":
                    return MaterialIconKind.FolderZip;
                case "application/vnd.ms-excel":
                    return MaterialIconKind.FileExcel;
                case "application/pdf":
                    return MaterialIconKind.FilePdfBox;
                case "text/plain":
                    return MaterialIconKind.FileText;
                default:
                    if (sourceData.StartsWith("text"))
                        return MaterialIconKind.Text;
                    if (sourceData.StartsWith("image"))
                        return MaterialIconKind.Image;
                    if (sourceData.StartsWith("application"))
                        return MaterialIconKind.Application;
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