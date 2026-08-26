using System;
using System.Collections.Generic;
using System.Linq;

namespace NetRisk.Packaging;

/// <summary>
/// Scrubs secrets out of anything about to be logged. Signing commands necessarily carry
/// passwords and API keys on their command line, and a build log is a published artifact.
/// </summary>
public static class SecretRedactor
{
    public const string Placeholder = "***";

    /// <summary>
    /// Replaces every occurrence of every non-empty secret with <see cref="Placeholder"/>.
    /// Longest secrets first, so a secret that contains another one still redacts cleanly.
    /// </summary>
    public static string Redact(string? text, IEnumerable<string?>? secrets)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        if (secrets is null)
            return text;

        var ordered = secrets
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .Distinct()
            .OrderByDescending(s => s.Length)
            .ToList();

        var result = text!;
        foreach (var secret in ordered)
            result = result.Replace(secret, Placeholder, StringComparison.Ordinal);

        return result;
    }
}
