using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Model;

namespace GUIClient.Converters;

public class StringIntStatusToStringConverter: IValueConverter
{
    public static readonly StringIntStatusToStringConverter Instance = new();
    
    public object? Convert( object? value, 
        Type targetType, 
        object? parameter, 
        CultureInfo culture)
    {
        if (value is null) return "";

        var canBeInt = Int32.TryParse(value.ToString(), out var intVal);
        
        if (!canBeInt) return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
        
        if (targetType.IsAssignableTo(typeof(string)))
        {
            
            switch (intVal)
            {
                case (int) IntStatus.Open:
                    return "Open";
                case (int) IntStatus.Closed:
                    return "Closed";
                case (int) IntStatus.Critical:
                    return "Critical";
                case (int) IntStatus.New:
                    return "New";
                case (int) IntStatus.Duplicated:
                    return "Duplicate";
                case (int) IntStatus.Fixed:
                    return "Fixed";
                case (int) IntStatus.Mitigated:
                    return "Mitigated";
                case (int) IntStatus.Prioritized:
                    return "Prioritized";
                case (int) IntStatus.Rejected:
                    return "Rejected";
                case (int) IntStatus.Reopened:
                    return "Reopened";
                case (int) IntStatus.AwaitingFix:
                    return "Awaiting Fix";
                case (int) IntStatus.FixAvailable:
                    return "Fix Available";
                case (int) IntStatus.ManagementReview:
                    return "Management Review";
                case (int) IntStatus.MitigationPlanned:
                    return "Mitigation Planned";
                case (int) IntStatus.NotRelevant:
                    return "Not Relevant";
                case (int) IntStatus.UnderReview:
                    return "Under Review";
                case (int) IntStatus.AwaitingInternalResponse:
                    return "Awaiting Internal Response";
                case (int) IntStatus.AwaitingUserResponse:
                    return "Awaiting User Response";
                case (int) IntStatus.AwaitingVendorResponse:
                    return "Awaiting Vendor Response";
                case (int) IntStatus.FixInProgress:
                    return "Fix In Progress";
                case (int) IntStatus.FixNotApplicable:
                    return "Fix Not Applicable";
                case (int) IntStatus.FixNotAvailable:
                    return "Fix Not Available";
                case (int) IntStatus.FixNotPossible:
                    return "Fix Not Possible";
                case (int) IntStatus.FixNotRequired:
                    return "Fix Not Required";
                case (int) IntStatus.SentForPatch:
                    return "Sent For Patch";
                default:
                    return "Unrecognized status";

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