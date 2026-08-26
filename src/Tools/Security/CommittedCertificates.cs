using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tools.Security;

/// <summary>
/// Recognises the TLS material that ships inside the NetRisk repository, so no host serves with it
/// (Track 7 milestone 7.3.3, finding NR-2026-003).
///
/// <c>src/API/Certificates/</c> and <c>src/WebSite/Certificates/</c> contain committed <c>.pfx</c>
/// and <c>.pem</c> files — self-signed, expired since 2023, private keys included. Having those for
/// local development is unremarkable. What is not is that the shipped <c>appsettings.json</c> in both
/// projects pointed at one of them with the password <c>"pass"</c>, in the very file that becomes the
/// deployment template. An installation that changed nothing was serving with a private key held by
/// everyone who has ever cloned the repository — which is no transport security at all against anyone
/// who bothers to look.
///
/// The guard refuses those specific, publicly known values rather than warning about them. A
/// start-up warning is read once and then lives in a log nobody tails, and the whole point of this
/// finding is that the insecure configuration is the one you get by doing nothing.
/// </summary>
public static class CommittedCertificates
{
    /// <summary>Setting that permits the committed material. Meant for local development only.</summary>
    public const string AllowKey = "Security:AllowDevelopmentCertificate";

    /// <summary>
    /// The file names committed under the two <c>Certificates</c> directories. Compared on the file
    /// name alone: an operator who copies <c>certificate.pfx</c> to <c>/etc/netrisk/</c> has still
    /// deployed the repository's private key.
    /// </summary>
    public static IReadOnlyList<string> FileNames { get; } =
    [
        "certificate.pfx", "certificate.pem", "localhost.pfx", "localhost.key", "localhost.cer",
        "demowebapp.local.pfx", "key.pem"
    ];

    /// <summary>The passwords those files are committed with, plus the usual placeholders.</summary>
    public static IReadOnlyList<string> Passwords { get; } = ["pass", "password", "changeit", "netrisk"];

    /// <summary>
    /// Checks a resolved certificate configuration.
    /// </summary>
    /// <param name="certificateFile">The configured <c>https:certificate:file</c>.</param>
    /// <param name="certificatePassword">The configured <c>https:certificate:password</c>.</param>
    /// <param name="allowed">Whether the committed material is permitted (local development).</param>
    /// <returns>The reason to refuse to start, or null when the configuration is acceptable.</returns>
    public static string? Inspect(string? certificateFile, string? certificatePassword, bool allowed)
    {
        if (allowed) return null;

        var problems = new List<string>();

        var fileName = FileNameOf(certificateFile);

        if (fileName != null && FileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            problems.Add($"'{fileName}' is a certificate committed to the NetRisk repository, so its "
                         + "private key is public");

        if (certificatePassword != null && Passwords.Contains(certificatePassword, StringComparer.Ordinal))
            problems.Add($"the certificate password '{certificatePassword}' is the placeholder from the "
                         + "shipped appsettings.json");

        if (problems.Count == 0) return null;

        return "Refusing to start: " + string.Join("; and ", problems)
               + ". Point https:certificate:file at a certificate issued for this host and supply its "
               + "password through an environment variable or user-secrets — see "
               + "docs/security/SECRETS.md. For local development set " + AllowKey + "=true.";
    }

    /// <summary>
    /// The last path segment, splitting on both separators.
    ///
    /// <see cref="Path.GetFileName(string)"/> alone is not enough: on Unix a backslash is an ordinary
    /// filename character, so a Windows-style path in a configuration file read on a Linux host would
    /// come back whole and the comparison would miss. Certificate paths travel between hosts in
    /// deployment templates, so this is worth two lines.
    /// </summary>
    private static string? FileNameOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var segments = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

        return segments.Length == 0 ? null : segments[^1];
    }

    /// <summary>
    /// Applies <see cref="Inspect"/> and throws when it objects, so the failure is a refusal to boot
    /// rather than a service that looks healthy while serving with a public key.
    /// </summary>
    public static void Enforce(string? certificateFile, string? certificatePassword, bool allowed)
    {
        var problem = Inspect(certificateFile, certificatePassword, allowed);
        if (problem != null) throw new InvalidOperationException(problem);
    }
}
