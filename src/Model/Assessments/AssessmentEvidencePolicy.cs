using System;
using System.IO;
using System.Linq;

namespace Model.Assessments;

/// <summary>
/// The limits on a comment and on evidence attached to one answer of an assessment run
/// (GitHub #80, T297, S44 D1/D5), and the pure normalisation both sides apply.
///
/// One definition shared by the server, which enforces it, and the desktop client, which checks it
/// before uploading so the assessor gets a clear message instead of a refused request. The client's
/// check is a convenience; the server's is the control.
/// </summary>
public static class AssessmentEvidencePolicy
{
    /// <summary>Longest comment accepted on one answer, in characters.</summary>
    public const int MaxCommentLength = 4000;

    /// <summary>Largest evidence file accepted: 20 MiB.</summary>
    public const long MaxEvidenceBytes = 20L * 1024 * 1024;

    /// <summary>Most evidence files one answer may carry.</summary>
    public const int MaxEvidencePerAnswer = 10;

    /// <summary>Width of <c>nr_files.name</c>; a longer name is cut, keeping its extension.</summary>
    public const int MaxFileNameLength = 100;

    /// <summary>
    /// The comment as stored: trimmed, and null when nothing but whitespace is left, so "no comment" has
    /// one representation.
    /// </summary>
    public static string? NormalizeComment(string? comment)
    {
        if (comment is null) return null;
        var trimmed = comment.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>Whether a (normalised) comment fits <see cref="MaxCommentLength"/>.</summary>
    public static bool IsCommentWithinLimit(string? comment) =>
        comment is null || comment.Length <= MaxCommentLength;

    /// <summary>Whether a file of <paramref name="bytes"/> bytes may be attached: not empty, not over the cap.</summary>
    public static bool IsSizeAccepted(long bytes) => bytes > 0 && bytes <= MaxEvidenceBytes;

    /// <summary>
    /// The name a file is stored under: its last path segment only — whatever directory the client
    /// sent is dropped, whichever separator it used — with control characters removed, and cut to
    /// <see cref="MaxFileNameLength"/> keeping the extension. Null when nothing usable is left.
    /// </summary>
    public static string? NormalizeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        // Both separators, whatever the server's platform: a Windows client sends backslashes and
        // Path.GetFileName on Linux would keep them.
        var lastSeparator = name.LastIndexOfAny(['/', '\\']);
        var segment = lastSeparator >= 0 ? name[(lastSeparator + 1)..] : name;

        var cleaned = new string(segment.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (cleaned.Length == 0 || cleaned == "." || cleaned == "..") return null;

        if (cleaned.Length <= MaxFileNameLength) return cleaned;

        var extension = Path.GetExtension(cleaned);
        if (extension.Length >= MaxFileNameLength / 2) extension = string.Empty;
        return cleaned[..(MaxFileNameLength - extension.Length)] + extension;
    }
}
