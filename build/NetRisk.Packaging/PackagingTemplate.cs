using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NetRisk.Packaging;

/// <summary>Raised when a template still holds placeholders after rendering.</summary>
public sealed class TemplateRenderException : Exception
{
    public TemplateRenderException(string message) : base(message)
    {
    }
}

/// <summary>
/// Renders the installer manifests kept under <c>build/installers/</c>. The manifests live on
/// disk as real, well-formed XML/YAML with <c>{{Token}}</c> placeholders so they can be linted
/// and reviewed as themselves; this class only substitutes the build-time values.
/// </summary>
public static class PackagingTemplate
{
    private static readonly Regex TokenPattern = new(@"\{\{(?<name>[A-Za-z0-9_.]+)\}\}", RegexOptions.Compiled);

    /// <summary>Every distinct placeholder name a template declares, in first-seen order.</summary>
    public static IReadOnlyList<string> Tokens(string? template)
    {
        if (string.IsNullOrEmpty(template))
            return Array.Empty<string>();

        var seen = new List<string>();
        foreach (Match match in TokenPattern.Matches(template))
        {
            var name = match.Groups["name"].Value;
            if (!seen.Contains(name, StringComparer.Ordinal))
                seen.Add(name);
        }

        return seen;
    }

    /// <summary>
    /// Substitutes every placeholder. An unsupplied placeholder is a build error rather than
    /// a literal "{{Version}}" shipped inside a manifest, which is the kind of defect that
    /// only surfaces when a customer's installer refuses to upgrade.
    /// </summary>
    public static string Render(string? template, IReadOnlyDictionary<string, string> values)
    {
        if (values is null)
            throw new ArgumentNullException(nameof(values));

        if (string.IsNullOrEmpty(template))
            return string.Empty;

        var missing = new List<string>();

        var rendered = TokenPattern.Replace(template, match =>
        {
            var name = match.Groups["name"].Value;
            if (values.TryGetValue(name, out var value))
                return value ?? string.Empty;

            if (!missing.Contains(name, StringComparer.Ordinal))
                missing.Add(name);

            return match.Value;
        });

        if (missing.Count > 0)
            throw new TemplateRenderException(
                "Template placeholders were left unresolved: " + string.Join(", ", missing.Select(m => "{{" + m + "}}")) + ".");

        return rendered;
    }
}
