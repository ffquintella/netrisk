using System;

namespace Tools.Security;

/// <summary>
/// Whether a URL may be handed to the operating system's default handler
/// (Track 7 finding NR-2026-023).
///
/// The desktop client opens links that came out of a scan report — a vulnerability's "see also"
/// reference, a plugin's advisory URL. Those strings are attacker-influenced: whoever produced the
/// <c>.nessus</c> file chose them. The client passed them straight to a shell-executing
/// <c>Process.Start</c>, where three things could go wrong:
///
///  * on Windows, <c>UseShellExecute = true</c> with an arbitrary <c>FileName</c> is "run this",
///    not "open this link" — a local path, a UNC path or an executable name all launch;
///  * on macOS, the command was <c>Process.Start("open", "-u " + url)</c>. The second argument is one
///    string the operating system re-splits, so a URL containing a space smuggles further arguments
///    to <c>open</c> — <c>-a</c> among them, which names an application to launch;
///  * on any platform, a non-web scheme (<c>file:</c>, <c>smb:</c>, a registered custom scheme) is
///    handled by whatever claimed it.
///
/// So the rule is an allowlist: an absolute URL, scheme <c>http</c> or <c>https</c>, no control
/// characters, of a sane length. Anything else is not opened.
/// </summary>
public static class ExternalUrlPolicy
{
    /// <summary>
    /// Generous but finite. A browser will not do anything useful with more than this, and an
    /// unbounded string on a command line is its own problem.
    /// </summary>
    private const int MaxLength = 4096;

    /// <summary>
    /// Whether <paramref name="url"/> may be opened, returning the parsed form when it may.
    /// </summary>
    public static bool TryParseOpenable(string? url, out Uri? parsed)
    {
        parsed = null;

        if (string.IsNullOrWhiteSpace(url)) return false;
        if (url.Length > MaxLength) return false;

        // Whitespace and control characters are what turn one argument into two. Rejected before
        // parsing, because Uri.TryCreate tolerates leading and trailing whitespace and would hand
        // back something that no longer matches the string we were given.
        foreach (var c in url)
            if (char.IsWhiteSpace(c) || char.IsControl(c))
                return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        parsed = uri;
        return true;
    }

    /// <summary>Convenience overload for callers that only need the verdict.</summary>
    public static bool IsOpenable(string? url) => TryParseOpenable(url, out _);
}
