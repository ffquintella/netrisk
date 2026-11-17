using System;
using System.Globalization;
using Avalonia.Data.Converters;
using GUIClient.Agents;

namespace GUIClient.Converters;

/// <summary>
/// T356: binds a control to its screen's <see cref="AgentFieldHighlights"/> with the control's identity as the parameter.
/// <see cref="IsLit"/> drives <c>Classes.agentChanged</c>; <see cref="Tip"/> the tooltip text that says why (so colour is
/// never the only signal). The decision is Core's (<see cref="AgentHighlightScreen"/>); this only reads it.
/// </summary>
public sealed class AgentHighlightConverter : IValueConverter
{
    private readonly bool _tip;

    private AgentHighlightConverter(bool tip) => _tip = tip;

    public static AgentHighlightConverter IsLit { get; } = new(false);

    public static AgentHighlightConverter Tip { get; } = new(true);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var control = parameter as string ?? "";
        if (value is not AgentFieldHighlights highlights) return _tip ? null : false;
        return _tip ? highlights.TipFor(control) : highlights.IsLit(control);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
