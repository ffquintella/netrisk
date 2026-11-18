using System;
using System.Globalization;
using GUIClient.Tools.RiskIndex;
using Model.Exceptions;

namespace GUIClient.ViewModels.Admin.RiskIndex;

/// <summary>The resource lookups and error wording the Risk index administration tabs share (S40 §6.0, §10.5).</summary>
internal static class RiskAdminText
{
    /// <summary>The text of a resource key, or null when no resource declares it.</summary>
    public static string? Optional(string key)
    {
        var localized = ViewModelBase.Localizer[key];
        return localized.ResourceNotFound ? null : localized.Value;
    }

    public static string Text(string key) => Optional(key) ?? key;

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Text(key), args);

    /// <summary>A refusal in the reader's language when its code is translated (<c>RiskApi_‹code›MSG</c>), else the server's sentence.</summary>
    public static string Explain(Exception ex) => ex switch
    {
        InvalidHttpRequestException { ErrorCode: { Length: > 0 } code } refusal =>
            RiskApiErrorText.Translate(code, refusal.Message, Optional, Text("RiskOverviewLoadError")),
        InvalidHttpRequestException refusal when !string.IsNullOrWhiteSpace(refusal.Message) && refusal.Message.Length <= 400 =>
            refusal.Message,
        DataNotFoundException => Text("ItemNotFoundMSG"),
        _ => Text("RiskOverviewLoadError")
    };

    /// <summary>The tooltip of a closed command: the server's sentence or the resource behind the gate's key.</summary>
    public static string Reason(Gate gate) => gate.Enabled ? "" : gate.ReasonText ?? (gate.ReasonKey is null ? "" : Text(gate.ReasonKey));

    public static string Day(DateTime? value) => value is { } v ? v.ToLocalTime().ToString("d", CultureInfo.CurrentCulture) : "—";

    public static string Moment(DateTime? value) => value is { } v ? v.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : "—";

    public static string Dash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
}
