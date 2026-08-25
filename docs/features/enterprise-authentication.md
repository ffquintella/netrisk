# Enterprise authentication

> Track 4 milestone 4.3. See also [authentication.md](authentication.md) for the local and
> FaceID paths.

Three capabilities, each independently switchable: federated sign-in (OIDC and SAML 2.0), automated
provisioning (SCIM 2.0), and phishing-resistant second factors (WebAuthn / FIDO2).

**Break-glass first:** local administrator login always remains available. An IdP outage must not be
able to lock administration out of the product.

## Federated sign-in

Multiple identity providers can be configured at once — a tenant that has just acquired another
company runs two IdPs for a year, and a product that allows exactly one forces them to keep local
passwords alive for half the organization.

### OIDC

Authorization code with PKCE, configured from the issuer's discovery document.

```
POST /IdentityProviders/{id}/oidc/signin   { "redirectUri": "http://127.0.0.1:51789/callback" }
   → { authorizationUrl, state, expiresInSeconds }

  … the client opens authorizationUrl in the system browser and waits on the loopback listener …

POST /IdentityProviders/oidc/callback      { "state": "...", "code": "..." }
   → { success, userId, provisioned, requiresSecondFactor }
```

The flow is explicit rather than ASP.NET Core's cookie-based `AddOpenIdConnect` middleware, because
the primary client is a desktop application: it opens the system browser and the IdP redirects to a
loopback URL (RFC 8252), and cookie middleware has nowhere to put a cookie in that shape. Discovery,
JWKS retrieval and id_token validation are still the `Microsoft.IdentityModel` libraries'.

Three properties carry the security of this flow:

* The **redirect URI must be loopback** or listed in `app:allowedRedirectUris`. Without that the
  endpoint is an open redirector an attacker points at their own host to collect codes.
* The **PKCE verifier stays on the server**, keyed by the state, so a client that cannot keep a secret
  does not have to.
* A **state is single-use**. Unknown, expired and already-redeemed are one answer, which is what makes
  an injected authorization code useless.

The id_token is validated for issuer, audience (the client id), lifetime and signature. A token minted
for another application at the same issuer is refused.

### SAML 2.0

Service-provider role, SP-initiated, HTTP-Redirect for the request and HTTP-POST for the response.
Metadata comes from a URL or from pasted XML — pasting is the documented fallback for a server that
cannot reach the IdP.

```
POST /IdentityProviders/{id}/saml/signin        → { authorizationUrl, state }
POST /IdentityProviders/{id}/saml/acs           (form: SAMLResponse, RelayState)
GET  /IdentityProviders/{id}/saml/metadata      → SP metadata to hand to the IdP
```

Validation covers every check whose absence has been a real-world SAML bypass:

* the signature verifies against a certificate **from the IdP's metadata** — never one embedded in the
  response, which an attacker controls;
* the signed element is the one being read (**signature wrapping** is refused);
* `Conditions` `NotBefore`/`NotOnOrAfter` with a bounded clock skew;
* the **audience** is this service provider, so an assertion minted for another SP is refused;
* `InResponseTo` matches the request NetRisk sent;
* DTD processing is **prohibited**, so an attacker-supplied response is not an XXE.

Unsigned assertions are refused unless a provider explicitly allows them, and every use of that
setting is logged as a warning.

An IdP-initiated response (no RelayState NetRisk issued) is accepted only when exactly one SAML
provider is configured. With two there is no way to know whose certificate should verify it, and
trying both would let a compromised IdP impersonate users of the other.

### Claim and group mapping

Per provider, because every IdP spells these differently and several spell them as URIs:

```json
{ "email": "email", "name": "name", "subject": "sub", "groups": "groups" }
```

```json
{ "Security-Admins": { "role": "Administrator", "admin": true, "entityId": 7 } }
```

Group names are matched **case-insensitively** — they differ in case between the directory and the
token more often than anyone expects. The mapping is reapplied on every sign-in, which is what makes
removing someone from an IdP group actually take away their NetRisk role.

**JIT provisioning is off by default.** An IdP that authenticates the whole company would otherwise
populate NetRisk with everyone who clicked the wrong tile.

## SCIM 2.0 provisioning

```
/scim/v2/ServiceProviderConfig
/scim/v2/Users    GET POST | /scim/v2/Users/{id}  GET PUT PATCH DELETE
/scim/v2/Groups   GET POST | /scim/v2/Groups/{id} GET PUT PATCH DELETE
```

Authenticated with a per-connection bearer token (`scim_…`), issued and revoked from the admin UI and
stored hashed — the secret is shown exactly once.

**Deactivation is the point of the whole milestone.** `active:false` sets both `Enabled = false` and
`Lockout = 1`, and every authenticated NetRisk request re-reads the user and requires both — so an IdP
deprovision revokes live sessions on the next request rather than at the next token expiry.

PATCH is implemented to RFC 7644, including the **path-less `replace` whose value is an object**,
which is what Entra ID sends. An implementation that only supports PUT, or that requires a path, never
disables anyone.

Supported filters are `attribute eq "value"` on `userName`, `externalId`, `emails` and `active` (and
`displayName` for groups). Anything else is refused with `scimType: invalidFilter` — silently ignoring
an unsupported filter would return the whole directory to a caller that asked for one user.

A duplicate `userName` is a **409 with `scimType: uniqueness`**, which is how an IdP learns to switch
from create to PATCH.

`DELETE` **deactivates** rather than removing the row: a NetRisk user is referenced by risks, findings
and audit history. The IdP sees the resource gone, which is what it asked for.

A SCIM **group is a NetRisk role** — the only group-shaped thing NetRisk has. Creating a group adopts
an existing role of the same name rather than failing, because an administrator has almost certainly
created it by hand already. Deleting a group empties it but keeps the role: an IdP removing a group
from its own scope is not a request to strip permissions from everyone else who holds it.

Every request — including a refused one — is written to `scim_request_logs`. "When did the IdP disable
this user, and did we acknowledge it" is a question asked during incidents.

## WebAuthn / FIDO2

```
POST /WebAuthn/register/begin | /register/complete       (authenticated)
POST /WebAuthn/assert/begin   | /assert/complete         (anonymous — this runs during sign-in)
POST /WebAuthn/recovery-codes/{userId}                   (administrator)
GET  /WebAuthn/status                                    (is the policy satisfied?)
```

The cryptography is [fido2-net-lib](https://github.com/passwordless-lib/fido2-net-lib)'s. What NetRisk
owns around it:

* a **challenge is single-use** and expires after five minutes;
* an assertion issued for one account cannot be completed with another account's authenticator;
* a **signature counter that does not advance** is refused as a possible cloned credential (a
  constant 0 is exempt, which is allowed by the spec and is what Apple's platform authenticator does);
* several named authenticators per user, with created and last-used dates, because a hardware key that
  only an administrator can replace is a lockout waiting to happen;
* revocation keeps the row — "which key was removed, and when" is an audit question.

**Recovery codes** are single-use, stored hashed, shown once, and generating a new batch invalidates
the unused old ones. Generation is administrator-only and logged as a warning, because a recovery code
is a way past the hardware factor.

**Desktop caveat.** WebAuthn is a browser API. Both ceremonies run through the same system-browser
flow as federated sign-in; there is no way to drive an authenticator from inside a native window.

## Configuration keys

| Key | Purpose |
|---|---|
| `app:baseUrl` | Default relying-party host, SP entity id and ACS URL |
| `app:allowedRedirectUris` | Non-loopback OIDC redirect URIs, comma-separated |
| `authentication:requireHardwareFactorForAdmins` | Enforce a hardware factor for administrative accounts |
| `authentication:webauthn:relyingPartyId` | Relying-party id (a domain). Must match the origin the ceremony page is served from |
| `authentication:webauthn:relyingPartyName` | Display name shown by the authenticator |
| `authentication:webauthn:origins` | Allowed origins, comma-separated |
| `authentication:webauthn:attestation` | `none` (default), `indirect`, `direct`, `enterprise` |

## Known limitations

* Pending sign-ins and WebAuthn ceremonies are held **in memory**. They live for minutes and must not
  survive a restart — a PKCE verifier persisted to a database is a credential at rest for no benefit —
  but a multi-instance deployment must pin the browser round trip to one instance.
* SAML **encrypted assertions** are not supported; configure the IdP to sign rather than encrypt.
* SAML single logout is recorded as a capability flag but is not implemented.
* Attestation defaults to `none`. Requiring attestation means maintaining an authenticator allow-list
  and the FIDO metadata service; a deployment that has not decided on one should not be unable to
  enrol a key.
* SCIM has no `externalId` column of its own; it is matched against the login.
