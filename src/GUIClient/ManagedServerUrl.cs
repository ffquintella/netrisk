using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace GUIClient;

/// <summary>
/// The server URL an administrator deployed, and whether it has to replace the one the user's
/// configuration store holds.
///
/// The client connects to the <c>Server</c> value of its per-user store, which the first-run
/// window fills in. <c>[Server] Url</c> in the overlays used to be bound and then never read, so
/// the MSI's <c>SERVERURL</c> and any configuration-management tool wrote a file with no effect.
/// A managed URL is now authoritative: it is copied into the store at every start-up, which
/// skips the first-run window and lets a changed deployment reach clients already configured.
///
/// Only the INI overlays count. The shipped appsettings.json carries a 127.0.0.1 placeholder,
/// and treating that as managed would overwrite every user's real server with it.
/// </summary>
public static class ManagedServerUrl
{
    /// <summary>The overlay key holding the URL.</summary>
    public const string ConfigurationKey = "Server:Url";

    /// <summary>
    /// Reads <c>Server:Url</c> from the INI overlays in <paramref name="sources"/>, later ones
    /// winning, relative to <paramref name="basePath"/>. Returns <c>null</c> when no overlay sets
    /// one, or when the value is not an absolute http/https URL.
    /// </summary>
    public static string? Resolve(string basePath, IReadOnlyList<ClientConfigurationSource> sources)
    {
        var builder = new ConfigurationBuilder().SetBasePath(basePath);

        foreach (var source in sources.Where(s => s.Format == ClientConfigurationFormat.Ini))
            builder.AddIniFile(source.FileName, optional: true);

        var url = builder.Build()[ConfigurationKey]?.Trim();

        return IsWellFormed(url) ? url : null;
    }

    /// <summary>Whether the store's <paramref name="stored"/> URL has to become <paramref name="managed"/>.</summary>
    public static bool ShouldReplace(string? stored, string? managed) =>
        managed != null && !string.Equals(stored, managed, StringComparison.Ordinal);

    // Same rule as the first-run window's Save button (LoadConfigurationViewModel), which this
    // file cannot reference without pulling Avalonia into the test project.
    private static bool IsWellFormed(string? url) =>
        !string.IsNullOrWhiteSpace(url) &&
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
