namespace RiskPortal.Services;

/// <summary>
/// The portal's own configuration. Bound from the <c>Portal</c> section, with <c>Server:Url</c> read
/// separately because it is the same key the desktop client uses and an operator configuring both
/// should not have to learn two names for it.
/// </summary>
public class PortalOptions
{
    public const string SectionName = "Portal";

    /// <summary>
    /// The client id the API's registration is keyed on. Stable across restarts: a fresh id on every
    /// start would ask an administrator to approve the portal again every time it is deployed.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>Reported at registration so an administrator can recognise what they are approving.</summary>
    public string? Hostname { get; set; }

    /// <summary>
    /// Where the generated client id is persisted when none is configured. Defaults under the
    /// installation's data directory rather than the working directory, which a service unit may not
    /// own.
    /// </summary>
    public string? DataDirectory { get; set; }

    /// <summary>
    /// Accept the API's TLS certificate without validating the chain. Debug only — a Release build
    /// ignores it, because a portal that trusts any certificate is a portal whose session token can be
    /// read off the wire by anything in the path.
    /// </summary>
    public bool AllowUntrustedApiCertificate { get; set; }
}
