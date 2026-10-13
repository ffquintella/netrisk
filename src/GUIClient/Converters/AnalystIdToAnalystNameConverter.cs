using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using ClientServices.Interfaces;
using GUIClient.Exceptions;

namespace GUIClient.Converters;

public class AnalystIdToAnalystNameConverter: BaseConverter, IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // A finding nobody has been assigned yet has no analyst: an empty cell, as TeamIdToTeamNameConverter does for a
        // finding with no fix team. This used to throw, so every unassigned finding on the risk screen logged a failed
        // binding (T331).
        if (value is null) return AvaloniaProperty.UnsetValue;
        
        if (value is int sourceId && targetType.IsAssignableTo(typeof(string)))
        {
            var usersService = GetService<IUsersService>();

            try
            {
                var user = usersService.GetUserName(sourceId);
                
                if (parameter != null && parameter is string variation)
                {
                    if(variation == "keepId") return $"{user} ({sourceId})";
                }
                
                return user;
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
            if (idx == "") return null;
            
            return int.Parse(idx);
        }

        //return 1;
        throw new NotSupportedException();
    }
    
}