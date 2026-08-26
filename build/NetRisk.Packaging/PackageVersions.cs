using System;
using System.Globalization;
using System.Linq;

namespace NetRisk.Packaging;

/// <summary>
/// Normalises the product version (owned by <c>src/Directory.Build.props</c>) into the shape
/// each packaging format demands. Every format has its own rules and every one of them fails
/// late and cryptically when violated, so they are validated here instead.
/// </summary>
public static class PackageVersions
{
    /// <summary>
    /// Accepts "2.16.3", "2.16.3.0" or a "Releases/2.16.3" git tag and returns the bare
    /// dotted numeric version.
    /// </summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Version must not be empty.", nameof(raw));

        var text = raw.Trim();

        // Tolerate the "Releases/x.y.z" tag form the build already passes around.
        var slash = text.LastIndexOf('/');
        if (slash >= 0)
            text = text.Substring(slash + 1);

        text = text.TrimStart('v', 'V');

        // Drop any semver pre-release/build metadata: no installer format accepts it.
        var dash = text.IndexOfAny(new[] { '-', '+' });
        if (dash >= 0)
            text = text.Substring(0, dash);

        var parts = text.Split('.');
        if (parts.Length is < 1 or > 4)
            throw new ArgumentException($"Version '{raw}' must have between one and four dotted components.", nameof(raw));

        foreach (var part in parts)
        {
            if (part.Length == 0 || !part.All(char.IsAsciiDigit))
                throw new ArgumentException($"Version '{raw}' has a non-numeric component '{part}'.", nameof(raw));
        }

        return string.Join(".", parts.Select(p => int.Parse(p, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>Major.Minor.Patch — the form used for artifact file names and the DMG/pkg.</summary>
    public static string ToThreePart(string? raw)
    {
        var parts = Split(raw, 3);
        return $"{parts[0]}.{parts[1]}.{parts[2]}";
    }

    /// <summary>
    /// Major.Minor.Build.Revision — required by MSIX. Each field is a UInt16 and the Store
    /// additionally requires Revision == 0 and Major > 0.
    /// </summary>
    public static string ToMsixVersion(string? raw)
    {
        var parts = Split(raw, 4);

        if (parts[0] == 0)
            throw new ArgumentException($"MSIX rejects a major version of 0 (got '{raw}').", nameof(raw));

        if (parts[3] != 0)
            throw new ArgumentException(
                $"MSIX reserves the revision field for the Store; it must be 0 (got '{raw}').", nameof(raw));

        foreach (var part in parts)
        {
            if (part > ushort.MaxValue)
                throw new ArgumentException(
                    $"MSIX version components must be <= {ushort.MaxValue} (got '{raw}').", nameof(raw));
        }

        return $"{parts[0]}.{parts[1]}.{parts[2]}.{parts[3]}";
    }

    /// <summary>
    /// MSI ProductVersion. Windows Installer only compares the first three fields and caps
    /// them at 255.255.65535 — anything larger is silently truncated, which breaks upgrades.
    /// </summary>
    public static string ToMsiProductVersion(string? raw)
    {
        var parts = Split(raw, 3);

        if (parts[0] > 255)
            throw new ArgumentException($"MSI major version must be <= 255 (got '{raw}').", nameof(raw));
        if (parts[1] > 255)
            throw new ArgumentException($"MSI minor version must be <= 255 (got '{raw}').", nameof(raw));
        if (parts[2] > 65535)
            throw new ArgumentException($"MSI build version must be <= 65535 (got '{raw}').", nameof(raw));

        return $"{parts[0]}.{parts[1]}.{parts[2]}";
    }

    /// <summary>Snap and AppStream both take the plain three-part version.</summary>
    public static string ToSnapVersion(string? raw) => ToThreePart(raw);

    /// <summary>
    /// CFBundleVersion / CFBundleShortVersionString. Apple wants at most three
    /// period-separated integers.
    /// </summary>
    public static string ToMacVersion(string? raw) => ToThreePart(raw);

    private static int[] Split(string? raw, int width)
    {
        var normalized = Normalize(raw);
        var parts = normalized.Split('.');

        // Narrowing is fine as long as the components being dropped are zero; "2.16.3.0" is
        // the same version as "2.16.3", but "2.16.3.4" is not.
        for (var i = width; i < parts.Length; i++)
        {
            if (int.Parse(parts[i], CultureInfo.InvariantCulture) != 0)
                throw new ArgumentException(
                    $"Version '{raw}' has more than {width} significant components and cannot be narrowed without losing information.",
                    nameof(raw));
        }

        var result = new int[width];
        for (var i = 0; i < Math.Min(width, parts.Length); i++)
            result[i] = int.Parse(parts[i], CultureInfo.InvariantCulture);

        return result;
    }
}
