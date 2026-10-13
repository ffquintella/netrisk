using System;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GUIClient.Exceptions;

namespace GUIClient.Converters;

public class StatusToColourConverter: IValueConverter
{
    public static readonly StatusToColourConverter Instance = new();
    
    public object? Convert( object? value, 
        Type targetType, 
        object? parameter, 
        CultureInfo culture)
    {
        // No status — nothing selected yet, or a selection just cleared — has no colour: leave the foreground to
        // the theme. This used to return "" (not a brush, so every start-up logged a failed conversion) and to
        // throw for an empty string (T324).
        if (value is null or string { Length: 0 }) return AvaloniaProperty.UnsetValue;

        if (value is string sourceData && targetType.IsAssignableTo(typeof(Avalonia.Media.IBrush)))
        {

            switch (sourceData)
            {
                case "New":
                    return new SolidColorBrush(Colors.LightGreen);
                // "Mgmt Reviewed" is the status the register actually stores (RiskHelper.GetRiskStatusName,
                // RiskWorkflowService); matching only "Management Review" painted every reviewed risk in the
                // Closed turquoise (T329). The long form is kept for any caller still passing it.
                case "Mgmt Reviewed":
                case "Management Review":
                    return new SolidColorBrush(Colors.Orange);
                case "Mitigation Planned":
                    return new SolidColorBrush(Colors.LightSalmon);
                default:
                    return new SolidColorBrush(Colors.Turquoise);
                    //throw new InvalidStatusException("Unrecognized status", sourceData);
                    //break;
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