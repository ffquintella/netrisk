using System;
using System.Net.Security;

namespace Tools.Security;

/// <summary>
/// Whether a NetRisk client accepts a server certificate that does not validate
/// (Track 7 milestone 7.4.1, finding NR-2026-004).
///
/// Every desktop-to-API call used to go through
/// <c>RemoteCertificateValidationCallback = (…) =&gt; true</c>, with a <c>//TODO: Remove this line</c>
/// beside it. That is a complete removal of transport authentication: anyone able to answer on the
/// configured host and port — a hostile DNS answer, a captive portal, an ARP spoof on a conference
/// network — could present any certificate and read and rewrite the whole session, including the
/// password in the basic-auth header.
///
/// The reason the bypass existed is real: NetRisk is self-hosted, and a new installation runs on a
/// self-signed certificate. So this type does not simply delete it — it turns it into an explicit,
/// per-installation, loudly logged opt-in that defaults to *off*, and it never silently applies.
///
/// The supported way to trust a private certificate authority is to install its root in the operating
/// system trust store, which makes validation succeed properly and is documented in
/// <c>docs/security/DATA_PROTECTION.md</c>. This switch is for the case where that has not happened
/// yet.
/// </summary>
public static class ServerCertificatePolicy
{
    /// <summary>The configuration key, in the client's <c>Server</c> section.</summary>
    public const string ConfigurationKey = "Server:AllowInvalidCertificate";

    /// <summary>
    /// The message logged, once, when the bypass is active. Asserted on by
    /// <c>ServerCertificatePolicyTest</c> so that it cannot quietly become a debug-level line.
    /// </summary>
    public const string BypassWarning =
        "TLS certificate validation is DISABLED for the NetRisk server connection because "
        + ConfigurationKey + " is set. The connection is not protected against interception. "
        + "Install the server's certificate authority in the operating system trust store and remove "
        + "this setting.";

    /// <summary>
    /// Resolves the opt-in from the two places a desktop client can carry it.
    ///
    /// Both, and in this order, because they answer different questions: the persisted client
    /// configuration is what an operator can change on an installed machine, and
    /// <c>Server:AllowInvalidCertificate</c> in <c>appsettings.json</c> is what they can ship with a
    /// managed deployment. Having only one of them is how a setting ends up documented and
    /// unreachable — the first-run server check read only the persisted store, so an operator
    /// following the error message's own instruction to set
    /// <c>Server:AllowInvalidCertificate</c> saw no change and the client could never be configured
    /// at all against a self-signed server.
    /// </summary>
    /// <param name="persistedValue">The value from the client's own configuration store, if any.</param>
    /// <param name="applicationSetting">The bound <c>Server:AllowInvalidCertificate</c> value.</param>
    public static bool Resolve(string? persistedValue, bool applicationSetting) =>
        bool.TryParse(persistedValue, out var persisted) ? persisted : applicationSetting;

    /// <summary>
    /// Whether a certificate presenting <paramref name="errors"/> may be accepted.
    /// </summary>
    /// <param name="errors">What the platform validator objected to.</param>
    /// <param name="allowInvalid">The installation's explicit opt-in.</param>
    /// <remarks>
    /// A clean chain is accepted whatever the flag says; anything else is accepted only when the
    /// operator has asked for it. The point of routing even the success case through here is that
    /// there is then exactly one place in the codebase that decides this, and one place a reviewer
    /// has to read.
    /// </remarks>
    public static bool ShouldAccept(SslPolicyErrors errors, bool allowInvalid) =>
        errors == SslPolicyErrors.None || allowInvalid;

    /// <summary>
    /// Builds the validation callback, invoking <paramref name="warn"/> exactly once if the bypass is
    /// in force.
    /// </summary>
    /// <returns>
    /// <c>null</c> when validation is enabled — deliberately, because handing the platform a
    /// callback that returns the platform's own answer replaces a well-tested code path with our
    /// own. A null callback means "use the default validation", which is what we want.
    /// </returns>
    public static RemoteCertificateValidationCallback? CreateCallback(bool allowInvalid, Action<string> warn)
    {
        if (!allowInvalid) return null;

        warn(BypassWarning);

        var warned = false;
        return (_, _, _, errors) =>
        {
            if (errors != SslPolicyErrors.None && !warned)
            {
                warned = true;
                warn(BypassWarning + $" (rejected by the platform as: {errors})");
            }

            return ShouldAccept(errors, allowInvalid: true);
        };
    }
}
