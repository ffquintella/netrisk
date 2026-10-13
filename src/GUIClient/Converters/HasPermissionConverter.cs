using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using GUIClient.Tools;

namespace GUIClient.Converters;

/// <summary>
/// Enables a write control only when the signed-in user may perform it — see <see cref="PermissionGate"/>, which holds
/// the rule, mirrors the API and fails closed. Bind it to the user (<c>AuthenticatedUserInfo</c>), not to the bare
/// permission list, so an administrator is admitted as the API admits one.
/// </summary>
public class HasPermissionConverter: IValueConverter
{
    public static readonly HasPermissionConverter Instance = new();

    public object? Convert(
        object? value, 
        Type targetType, 
        object? parameter, 
        CultureInfo culture)
    {
        // A boolean target (IsEnabled, IsVisible) always gets an answer, and an unrecognised input answers "no". An
        // error here would let the binding fall back to the property's default — true for IsEnabled — which is how
        // this gate used to fail open (T326).
        if (targetType == typeof(bool) || targetType == typeof(bool?) || targetType == typeof(object))
            return PermissionGate.Allows(value, parameter);

        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }
    
    public object ConvertBack( object? value, Type targetType, object? parameter, CultureInfo culture )
    {
        throw new NotSupportedException();
    }
}
