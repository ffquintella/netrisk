namespace Model.Authentication;

/// <summary>
/// One in-flight desktop SSO sign-in, held in the API's memory cache for ten minutes.
///
/// The shape of this record is load-bearing for Track 7 finding NR-2026-001. A desktop client cannot
/// participate in a browser redirect, so the flow is necessarily out-of-band: the client opens a
/// browser, the browser authenticates against the identity provider, and the client then collects the
/// resulting session token by naming this record. That makes the record's identifier a bearer
/// credential, and it makes *who is allowed to create one* the security boundary.
/// </summary>
public class SAMLRequest
{
    /// <summary>
    /// The identifier the desktop client polls with and the browser carries in its URL.
    ///
    /// Minted by the server (<c>GET /Authentication/SAMLRequestId</c>), never by the caller. It used
    /// to be a query parameter on the browser-facing endpoint, which meant an attacker could choose
    /// it, send a victim the link, and then redeem the victim's completed sign-in for themselves —
    /// no guessing required, so no amount of entropy would have helped.
    /// </summary>
    public string RequestToken { get; set; } = "";

    /// <summary>
    /// <c>requested</c> when minted, <c>accepted</c> once the person in the browser has explicitly
    /// approved it. Only <c>accepted</c> is redeemable.
    /// </summary>
    public string Status { get; set; } = "requested";

    /// <summary>The identity the browser authenticated as; the subject of the issued token.</summary>
    public string UserName { get; set; } = "";

    /// <summary>
    /// The approved client registration that asked for this sign-in.
    ///
    /// Recorded at mint time and required again at redemption, so that a token can only be collected
    /// by the device that started the flow. This is also what stops an anonymous outsider from
    /// creating a pending request at all: minting requires an administrator-approved client.
    /// </summary>
    public string ClientId { get; set; } = "";

    /// <summary>The hostname of that registration, shown on the approval page so the person approving can see which machine asked.</summary>
    public string ClientHostname { get; set; } = "";

    /// <summary>
    /// A single-use anti-forgery token for the approval form.
    ///
    /// The SAML session cookie is <c>SameSite=None</c> — it has to be, to survive the identity
    /// provider's cross-site POST back — which means a cross-site form submission would carry it.
    /// Without this token, a page under the attacker's control could auto-submit the approval on the
    /// victim's behalf and the consent screen would be decorative. The value only ever appears inside
    /// the page rendered to the authenticated browser, where the same-origin policy keeps it.
    /// </summary>
    public string ApprovalToken { get; set; } = "";
}
