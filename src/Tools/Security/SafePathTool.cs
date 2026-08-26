using System;
using System.IO;
using System.Linq;

namespace Tools.Security;

/// <summary>
/// Guards for building a filesystem path out of a value that came from a request
/// (Track 7 milestone 7.1.2, finding NR-2026-006).
///
/// <c>Path.Combine</c> is not a containment primitive: given a second argument of
/// <c>"../../etc"</c> it happily walks out of the first, and given a rooted second argument it
/// discards the first entirely. Every place that turns a caller-supplied identifier into a path has
/// to say so explicitly, which is what these two methods are for.
/// </summary>
public static class SafePathTool
{
    /// <summary>
    /// Whether <paramref name="identifier"/> is safe to use as a single path segment.
    ///
    /// The rule is an allowlist rather than a blocklist of dangerous sequences: letters, digits,
    /// dash, underscore and dot, no leading dot, at most 128 characters, and never a run of two
    /// dots. Blocklists lose to encoding tricks (<c>%2e%2e</c>, <c>..\</c>, alternate data streams,
    /// Unicode look-alikes); an allowlist of the characters a GUID actually needs does not.
    /// </summary>
    public static bool IsSafeSegment(string? identifier)
    {
        if (string.IsNullOrEmpty(identifier)) return false;
        if (identifier.Length > 128) return false;
        if (identifier[0] == '.') return false;
        if (identifier.Contains("..", StringComparison.Ordinal)) return false;

        return identifier.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_' || c == '.');
    }

    /// <summary>
    /// Combines <paramref name="baseDirectory"/> with <paramref name="segments"/> and proves the
    /// result is still inside the base directory.
    /// </summary>
    /// <remarks>
    /// The containment check is done on the fully resolved paths, so it also catches the cases a
    /// character allowlist cannot: a symlink planted inside the upload directory, or a base
    /// directory expressed relatively. It is belt-and-braces on purpose — this is the last line
    /// before a write.
    /// </remarks>
    /// <exception cref="ArgumentException">A segment is unsafe, or the result escapes the base.</exception>
    public static string CombineWithin(string baseDirectory, params string[] segments)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            throw new ArgumentException("The base directory must be set.", nameof(baseDirectory));

        foreach (var segment in segments)
            if (!IsSafeSegment(segment))
                throw new ArgumentException(
                    $"'{segment}' is not a valid path segment.", nameof(segments));

        var root = Path.GetFullPath(baseDirectory);
        var combined = Path.GetFullPath(Path.Combine(new[] { root }.Concat(segments).ToArray()));

        // The trailing separator matters: without it "/tmp/netrisk-api-evil" starts with
        // "/tmp/netrisk-api" and would pass.
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(rootWithSeparator, StringComparison.Ordinal))
            throw new ArgumentException(
                "The resolved path escapes the base directory.", nameof(segments));

        return combined;
    }
}
