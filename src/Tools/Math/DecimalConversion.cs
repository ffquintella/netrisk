using System.Globalization;

namespace Tools.Math;

/// <summary>
/// <c>double</c> to <c>decimal</c> with the rounding every release before .NET 11 applied (S56).
/// </summary>
/// <remarks>
/// Up to .NET 10, <c>(decimal)0.01d</c> was <c>0.01m</c>: the conversion rounded to 15 significant digits.
/// From .NET 11 it is exact, <c>0.0100000000000000002081668171m</c>, so a value that was typed as a short decimal
/// no longer equals its own literal, fails a "two decimal places" check and renders with 28 digits in a numeric
/// box. Use this wherever a double that came from a short decimal is shown or compared as a decimal.
/// </remarks>
public static class DecimalConversion
{
    private const string FifteenSignificantDigits = "G15";

    /// <summary>The nearest decimal at 15 significant digits; a non-finite value throws like the cast it replaces.</summary>
    public static decimal FromDouble(double value)
    {
        if (!double.IsFinite(value))
            throw new OverflowException("A non-finite double has no decimal value.");

        return decimal.Parse(value.ToString(FifteenSignificantDigits, CultureInfo.InvariantCulture),
            NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
