using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using GUIClient.Exceptions;
using Material.Icons;
using Model;

namespace GUIClient.Converters;

public class IntStatusToMaterialIconkindConverter: IValueConverter
{
    
    public static readonly IntStatusToMaterialIconkindConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);;

        if (value is ushort status && targetType.IsAssignableTo(typeof(MaterialIconKind)))
        {

           
            switch (status)
            {
                case (ushort) IntStatus.New:
                    return MaterialIconKind.NewReleases;
                case (ushort) IntStatus.Verified:
                    return MaterialIconKind.Verified;
                case (ushort) IntStatus.Rejected:
                    return MaterialIconKind.Denied;
                case (ushort) IntStatus.AwaitingFix:
                    return MaterialIconKind.Hours24;
                case (ushort) IntStatus.Closed:
                    return MaterialIconKind.CloseOctagon;
                case (ushort) IntStatus.Fixed:
                    return MaterialIconKind.BugCheck;
                case (ushort) IntStatus.Mitigated:
                    return MaterialIconKind.VacuumCleaner;
                case (ushort) IntStatus.NotRelevant:
                    return MaterialIconKind.FileDiscard;
                case (ushort) IntStatus.Retired:
                    return MaterialIconKind.HourglassEmpty;
                case (ushort) IntStatus.Duplicated:
                    return MaterialIconKind.ContentDuplicate;
                case (ushort) IntStatus.Outdated:
                    return MaterialIconKind.History;
                case (ushort) IntStatus.Deleted:
                    return MaterialIconKind.TrashCan;
                case (ushort) IntStatus.Prioritized:
                    return MaterialIconKind.PriorityHigh;
                case (ushort) IntStatus.Processing:
                    return MaterialIconKind.CogPlay;
                case (ushort) IntStatus.Error:
                    return MaterialIconKind.AlertCircle;
                default:
                    return MaterialIconKind.FlaskEmpty;
            }
        }
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}