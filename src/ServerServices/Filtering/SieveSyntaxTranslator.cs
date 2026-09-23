using System.Text;

namespace ServerServices.Filtering;

/// <summary>
/// Rewrites a Sieve filter or sort expression into Gridify's syntax. Users type filters into the
/// findings screen and the client persists them per user, so the expressions outlive the library
/// that parsed them. See CHANGELOG.md, [NEXT].
/// </summary>
public static class SieveSyntaxTranslator
{
    /// <summary>
    /// Sieve operator to Gridify operator. Order matters: longest match wins, or <c>@=*</c> reads
    /// as <c>@=</c> followed by a stray <c>*</c>.
    /// </summary>
    private static readonly (string Sieve, string Gridify, bool CaseInsensitive)[] Operators =
    [
        ("!@=*", "!*", true),
        ("!_=*", "!^", true),
        ("!@=",  "!*", false),
        ("!_=",  "!^", false),
        ("!=*",  "!=", true),
        ("@=*",  "=*", true),
        ("_=*",  "^",  true),
        ("==*",  "=",  true),
        ("@=",   "=*", false),
        ("_=",   "^",  false),
        ("==",   "=",  false),
        ("!=",   "!=", false),
        (">=",   ">=", false),
        ("<=",   "<=", false),
        (">",    ">",  false),
        ("<",    "<",  false),
    ];

    /// <summary>Characters Gridify treats as syntax inside a value.</summary>
    private const string GridifyReserved = ",|()";

    /// <summary>
    /// Translate a Sieve <c>filters</c> expression. Null for blank input, which Gridify reads as
    /// "no filter".
    /// </summary>
    public static string? TranslateFilter(string? sieveFilter)
    {
        if (string.IsNullOrWhiteSpace(sieveFilter)) return null;

        var translated = SplitUnescaped(sieveFilter, ',')
            .Select(TranslateSingleFilter)
            .Where(f => !string.IsNullOrEmpty(f))
            .ToList();

        return translated.Count == 0 ? null : string.Join(",", translated);
    }

    /// <summary>
    /// Translate a Sieve <c>sorts</c> expression. Sieve marks descending with a leading <c>-</c>;
    /// Gridify uses a trailing <c>desc</c>.
    /// </summary>
    public static string? TranslateSort(string? sieveSort)
    {
        if (string.IsNullOrWhiteSpace(sieveSort)) return null;

        var terms = sieveSort.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(term => term.Trim())
            .Where(term => term.Length > 0)
            .Select(term => term.StartsWith('-') ? $"{term[1..].Trim()} desc" : term)
            .Where(term => term != "desc" && term != " desc")
            .ToList();

        return terms.Count == 0 ? null : string.Join(",", terms);
    }

    private static string TranslateSingleFilter(string clause)
    {
        var (name, op, value) = Split(clause);
        if (op is null) return string.Empty;

        // Sieve's `field==a|b|c` means "field is any of a, b, c". Gridify has no value list, so it
        // becomes an OR of whole conditions, parenthesised to stay atomic against a sibling AND.
        var values = SplitUnescaped(value, '|').ToList();

        var suffix = op.Value.CaseInsensitive ? "/i" : string.Empty;
        var conditions = values
            .Select(v => $"{Escape(name)}{op.Value.Gridify}{Escape(Unescape(v))}{suffix}")
            .ToList();

        return conditions.Count == 1 ? conditions[0] : $"({string.Join("|", conditions)})";
    }

    /// <summary>Name, operator and value, splitting on the first operator that matches.</summary>
    private static (string Name, (string Sieve, string Gridify, bool CaseInsensitive)? Op, string Value)
        Split(string clause)
    {
        foreach (var op in Operators)
        {
            var at = clause.IndexOf(op.Sieve, StringComparison.Ordinal);
            if (at <= 0) continue;

            return (clause[..at].Trim(), op, clause[(at + op.Sieve.Length)..]);
        }

        return (clause, null, string.Empty);
    }

    /// <summary>Split on a separator, honouring Sieve's backslash escape.</summary>
    private static IEnumerable<string> SplitUnescaped(string input, char separator)
    {
        var current = new StringBuilder();

        for (var i = 0; i < input.Length; i++)
        {
            if (input[i] == '\\' && i + 1 < input.Length)
            {
                current.Append(input[i]).Append(input[i + 1]);
                i++;
                continue;
            }

            if (input[i] == separator)
            {
                yield return current.ToString();
                current.Clear();
                continue;
            }

            current.Append(input[i]);
        }

        yield return current.ToString();
    }

    /// <summary>Drop Sieve's escaping so the raw value can be re-escaped for Gridify.</summary>
    private static string Unescape(string value)
    {
        var result = new StringBuilder();

        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                result.Append(value[i + 1]);
                i++;
                continue;
            }

            result.Append(value[i]);
        }

        return result.ToString();
    }

    /// <summary>Escape the characters Gridify would otherwise read as syntax.</summary>
    private static string Escape(string value)
    {
        var result = new StringBuilder();

        foreach (var c in value)
        {
            if (GridifyReserved.Contains(c)) result.Append('\\');
            result.Append(c);
        }

        return result.ToString();
    }
}
