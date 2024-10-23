using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Data;
using Avalonia.Data.Converters;
using ClientServices.Interfaces;
using Model.Exceptions;

namespace GUIClient.Converters;

public class HostIdToNameConverter: BaseConverter, IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) throw new InvalidParameterException("hostId","Invalid null host id to convert");
        
        if (value is int sourceId && targetType.IsAssignableTo(typeof(string)))
        {
            var hostsService = GetService<IHostsService>();

            try
            {
                var host = hostsService.GetOne(sourceId);

                if (host == null) return "";
                
                if (parameter != null && parameter is string variation)
                {
                    if(variation == "keepId") return $"{host.HostName} ({sourceId})";
                }
                
                return host.HostName;
            }
            catch 
            {
                return "";
            }

        }
        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string sourceText && targetType.IsAssignableTo(typeof(int?)))
        {
            var pattern = @"\((?<idx>.+)\)";
            
            // Instantiate the regular expression object.
            var r = new Regex(pattern, RegexOptions.IgnoreCase);

            // Match the regular expression pattern against a text string.
            var m = r.Match(sourceText);
            var idx = m.Groups["idx"].Value;
            return int.Parse(idx);
        }

        //return 1;
        throw new NotSupportedException();
    }
}