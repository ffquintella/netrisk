using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using GUIClient.ViewModels;

namespace GUIClient.Converters;

/// <summary>
/// Builds the tooltip for a gated toolbar button: the action's own name while it is available,
/// and the action's name plus the reason it is blocked while it is not.
///
/// IX-4 requires a blocked button to be "visible but disabled <b>with a ToolTip.Tip stating
/// why</b>". Bind two values — the action name and the button's enabled flag — and pass the
/// reason kind as the converter parameter: <c>permission</c> (default) or <c>status</c>.
/// </summary>
public class ActionTooltipConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var actionName = values.Count > 0 ? values[0] as string ?? string.Empty : string.Empty;
        var isEnabled = values.Count > 1 && values[1] is true;

        if (isEnabled) return actionName;

        var reasonKey = parameter as string == "status"
            ? "NotAvailableForCurrentStatusMSG"
            : "NoPermissionForActionMSG";

        var reason = ViewModelBase.Localizer[reasonKey].ToString();

        return string.IsNullOrEmpty(actionName) ? reason : $"{actionName} — {reason}";
    }
}
