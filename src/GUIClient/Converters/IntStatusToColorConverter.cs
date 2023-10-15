using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GUIClient.Exceptions;
using Material.Icons;
using Model;

namespace GUIClient.Converters;

public class IntStatusToColorConverter: IValueConverter
{
    
    public static readonly IntStatusToColorConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);;

        if (value is ushort status && targetType.IsAssignableTo(typeof(Avalonia.Media.IBrush)))
        {

            switch (status)
            {
                case 0:
                    return new SolidColorBrush(Colors.Gainsboro); 
                case 1:
                    return new SolidColorBrush(Colors.LightBlue);
                case 2:
                    return new SolidColorBrush(Colors.Aquamarine);
                case 3:
                    return new SolidColorBrush(Colors.MediumPurple);
                case 4:
                    return new SolidColorBrush(Colors.Black);
                case 5:
                    return new SolidColorBrush(Colors.LightSalmon);
                case 6:
                    return new SolidColorBrush(Colors.Gray);
                case 7:
                    return new SolidColorBrush(Colors.Green);
                case 8:
                    return new SolidColorBrush(Colors.Lime);
                case 9:
                    return new SolidColorBrush(Colors.LightGreen);
                case 10:
                    return new SolidColorBrush(Colors.Orange);
                case 11:
                    return new SolidColorBrush(Colors.SaddleBrown);
                case 12:
                    return new SolidColorBrush(Colors.SandyBrown);
                case 13:
                    return new SolidColorBrush(Colors.Red);
                case 14:
                    return new SolidColorBrush(Colors.OrangeRed);
                case 15:
                    return new SolidColorBrush(Colors.DodgerBlue);
                case 16:
                    return new SolidColorBrush(Colors.CadetBlue);
                case 17:
                    return new SolidColorBrush(Colors.DarkBlue);
                case 18:
                    return new SolidColorBrush(Colors.RoyalBlue);
                case 19:
                    return new SolidColorBrush(Colors.LightSeaGreen);
                case 20:
                    return new SolidColorBrush(Colors.SpringGreen);
                case 21:
                    return new SolidColorBrush(Colors.SeaGreen);
                case 22:
                    return new SolidColorBrush(Colors.ForestGreen);
                case 23:
                    return new SolidColorBrush(Colors.DarkOliveGreen);
                case 24:
                    return new SolidColorBrush(Colors.MediumSpringGreen);
                case 25:
                    return new SolidColorBrush(Colors.LawnGreen);
                case 26:
                    return new SolidColorBrush(Colors.LawnGreen);
                case 27:
                    return new SolidColorBrush(Colors.Fuchsia);
                case 28:
                    return new SolidColorBrush(Colors.LightCoral);
                case 29:
                    return new SolidColorBrush(Colors.HotPink);
                case 30:
                    return new SolidColorBrush(Colors.DarkOrange);
                case 31:
                    return new SolidColorBrush(Colors.IndianRed);
                case 32:
                    return new SolidColorBrush(Colors.DarkKhaki);
                case 33:
                    return new SolidColorBrush(Colors.DarkOrchid);
                case 34:
                    return new SolidColorBrush(Colors.LightGoldenrodYellow);
                case 35:
                    return new SolidColorBrush(Colors.FloralWhite);
                case 36:
                    return new SolidColorBrush(Colors.Lime);
                case 37:
                    return new SolidColorBrush(Colors.Brown);
                case 38:
                    return new SolidColorBrush(Colors.Yellow);
                case 39:
                    return new SolidColorBrush(Colors.Yellow);
                case 40:
                    return new SolidColorBrush(Colors.Orange);
                case 41:
                    return new SolidColorBrush(Colors.YellowGreen);
                case 42:
                    return new SolidColorBrush(Colors.SpringGreen);
                default:
                    return new SolidColorBrush(Colors.DarkSlateGray);
            }
        }
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}