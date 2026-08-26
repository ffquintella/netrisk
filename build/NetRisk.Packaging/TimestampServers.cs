using System;
using System.Collections.Generic;
using System.Linq;

namespace NetRisk.Packaging;

/// <summary>
/// RFC 3161 timestamp endpoints. Signing without a timestamp produces a signature that dies
/// with the certificate, and a single timestamp authority going down is the classic flaky
/// release build — so the build always has an ordered fallback list.
/// </summary>
public static class TimestampServers
{
    public static readonly IReadOnlyList<string> Defaults = new[]
    {
        "http://timestamp.acs.microsoft.com",
        "http://timestamp.digicert.com",
        "http://timestamp.sectigo.com"
    };

    /// <summary>
    /// Builds the ordered, de-duplicated list to try: the caller's primary first, then any
    /// caller-supplied extras (comma or semicolon separated), then the built-in defaults.
    /// </summary>
    public static IReadOnlyList<string> Resolve(string? primary, string? additional)
    {
        var ordered = new List<string>();

        void Add(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return;

            var value = candidate.Trim();
            if (!ordered.Contains(value, StringComparer.OrdinalIgnoreCase))
                ordered.Add(value);
        }

        Add(primary);

        if (!string.IsNullOrWhiteSpace(additional))
        {
            foreach (var part in additional.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                Add(part);
        }

        foreach (var fallback in Defaults)
            Add(fallback);

        return ordered;
    }
}
