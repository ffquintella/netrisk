

namespace Tools.Math;

public static class DivisionHelper
{
    public static double RoundedDivision(double a, double b, double threshold = 0.6)
    {
        var div = a / b;
        var temp = System.Math.Floor(div);
        var fractional = div - temp;

        return fractional > threshold ? System.Math.Ceiling(div) : System.Math.Floor(div);
    }
}