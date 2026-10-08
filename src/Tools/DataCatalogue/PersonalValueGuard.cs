using System.Text.RegularExpressions;

namespace Tools.DataCatalogue;

/// <summary>
/// A heuristic that keeps a data subject's values out of the LGPD catalogue (Stage 9.11, S52 D13). The catalogue describes
/// kinds of data — "CPF of the student", "institutional e-mail" — and a free text that carries an actual e-mail address or
/// a formatted CPF was almost certainly pasted from a record. It is refused, never stored and never logged.
///
/// Deliberately narrow (S52 R1): it does not catch a name, a phone number or an unformatted CPF, because those collide with
/// ordinary text and document numbers. The control is the design — a catalogue of kinds — and the DPO's review; this
/// catches the common accident.
/// </summary>
public static class PersonalValueGuard
{
    /// <summary>An e-mail address: something@domain.tld.</summary>
    private static readonly Regex Email = new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    /// <summary>A CPF in its formatted shape, 000.000.000-00.</summary>
    private static readonly Regex FormattedCpf = new(@"(?<!\d)\d{3}\.\d{3}\.\d{3}-\d{2}(?!\d)",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    /// <summary>True when <paramref name="text"/> looks like it carries an e-mail address or a formatted CPF.</summary>
    public static bool LooksLikePersonalValue(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        try
        {
            return Email.IsMatch(text) || FormattedCpf.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            // A text that takes this long to scan is refused rather than stored unchecked.
            return true;
        }
    }
}
