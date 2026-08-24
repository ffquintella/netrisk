namespace Model.Findings;

/// <summary>
/// The scopes a CI API token can be granted (Track 3 milestone 3.5.1).
///
/// A closed, small set on purpose. Scopes exist so a pipeline identity can be least-privilege — a
/// build that uploads scan results has no business reading the risk register — and a scope
/// vocabulary that grows with every endpoint stops being something an operator can reason about.
/// </summary>
public static class ApiTokenScopes
{
    /// <summary>Upload scan reports. The scope a CI pipeline actually needs.</summary>
    public const string VulnerabilitiesImport = "vulnerabilities:import";

    /// <summary>Read findings, including an import's own results — what a gate check needs.</summary>
    public const string VulnerabilitiesRead = "vulnerabilities:read";

    /// <summary>Change a finding's triage status. Deliberately separate from import.</summary>
    public const string VulnerabilitiesWrite = "vulnerabilities:write";

    /// <summary>Read the risk register.</summary>
    public const string RisksRead = "risks:read";

    public static readonly string[] All =
    [
        VulnerabilitiesImport, VulnerabilitiesRead, VulnerabilitiesWrite, RisksRead
    ];

    /// <summary>
    /// Parses the stored comma-separated form. Unknown scopes are dropped rather than honoured: a
    /// typo must not grant anything, and it must not deny everything either.
    /// </summary>
    public static string[] Parse(string? scopes)
    {
        if (string.IsNullOrWhiteSpace(scopes)) return [];

        return scopes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => All.Contains(s, StringComparer.OrdinalIgnoreCase))
            .Select(s => s.ToLowerInvariant())
            .Distinct()
            .ToArray();
    }

    /// <summary>Names not in <see cref="All"/>, for a clear error at issue time.</summary>
    public static string[] Unknown(string? scopes)
    {
        if (string.IsNullOrWhiteSpace(scopes)) return [];

        return scopes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !All.Contains(s, StringComparer.OrdinalIgnoreCase))
            .Distinct()
            .ToArray();
    }
}

/// <summary>
/// A newly issued token. The secret appears here and nowhere else, ever: it is not stored, and
/// there is no endpoint that can return it again.
/// </summary>
public class IssuedApiToken
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>The public key id, safe to log and to show in a list.</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>
    /// The full token, as the caller must send it in <c>Authorization: Bearer</c>. Shown once.
    /// </summary>
    public string Secret { get; set; } = string.Empty;

    public string[] Scopes { get; set; } = [];

    public DateTime? ExpiresAt { get; set; }

    public int? EntityId { get; set; }
}
