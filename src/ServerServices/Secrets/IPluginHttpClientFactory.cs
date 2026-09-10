using Contracts.Secrets;
using ServerServices.Interfaces;

namespace ServerServices.Secrets;

/// <summary>
/// Builds the HTTP seam handed to a secret-vault plugin for one connection.
///
/// A factory rather than a single injected <see cref="IPluginHttpClient"/> because one property of
/// that seam — whether TLS certificate validation is skipped — is a per-connection setting an
/// operator makes, and an installation with a public vault and an internal one must not have to
/// choose one answer for both.
/// </summary>
public interface IPluginHttpClientFactory
{
    /// <param name="allowInvalidCertificate">
    /// True only when the vault connection being served has the option set. Everything else about the
    /// seam — the SSRF destination policy, timeouts, logging — is unchanged either way.
    /// </param>
    IPluginHttpClient Create(bool allowInvalidCertificate);
}

/// <inheritdoc />
public class PluginHttpClientFactory(IOutboundHttpClient outbound) : IPluginHttpClientFactory
{
    public IPluginHttpClient Create(bool allowInvalidCertificate) =>
        new PluginHttpClientAdapter(outbound, allowInvalidCertificate);
}
