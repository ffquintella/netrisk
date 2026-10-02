using System;
using System.Globalization;
using Avalonia.Data.Converters;
using DAL.Entities;
using GUIClient.Tools.Hosts;

namespace GUIClient.Converters;

/// <summary>
/// The icon for a host service chip or Services-tab row (S38 §3.4). Accepts the service entity or
/// the view's <see cref="HostServiceRow"/>; the table itself is <see cref="ServiceIconMap"/>.
/// </summary>
public class ServiceToIconConverter : IValueConverter
{
    public static readonly ServiceToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        HostServiceRow row => ServiceIconMap.KindFor(row.Name, row.Port),
        HostsService service => ServiceIconMap.KindFor(service.Name, service.Port),
        _ => ServiceIconMap.Fallback
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
