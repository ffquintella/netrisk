# NetRisk Security Findings Register

> Track 7 milestone 7.1.3 · Audit window 2026-08-26 · Baseline commit `756c0322` (Track 5 complete)
> **Updated 2026-08-26 (Track 8):** the five findings Track 7 left open — NR-2026-008b, 017, 025, 028,
> 032 — are closed, each with a named regression test. NR-2026-027 remains accepted; its proposed
> mitigation is now implemented. No finding is open.
> Severity by the [OWASP Risk Rating Methodology](https://owasp.org/www-community/OWASP_Risk_Rating_Methodology) — likelihood × impact, stated per finding rather than asserted.
> Method and boundaries: [THREAT_MODEL.md](THREAT_MODEL.md) · Requirement-by-requirement checklist: [ASVS_L2_CHECKLIST.md](ASVS_L2_CHECKLIST.md) · Burn-down: [BURN_DOWN.md](BURN_DOWN.md)

## How to read this

Every finding was established **by reading the code and, where the fix landed, by a test that fails
on the pre-fix code**. That is not a formality. This repository has twice shipped a security control
that was documented as working and was not:

* multi-entity scoping was described as "enforced server-side" while `ApplyEntityScope` was called
  from exactly one query and the controller never passed it a principal, so it always received null
  and filtered nothing — every authenticated user could read every tenant's data (fixed in
  `a386faaf`);
* the Master Dashboard backend was marked complete when no endpoint or service existed.

So each entry below states **how it was established**, and each fixed entry names the regression
test. A finding whose evidence is "the comment says so" is not in this register.

The audit itself found the same pattern again: `WebAuthnController`'s doc comment stated "The
registration endpoints are authenticated" and there was no `[Authorize]` attribute anywhere on the
class (NR-2026-009).

## Status summary

Three states. **Fixed** means the code changed and a test pins it. **Open** means it is not fixed and
what closing it requires is written down. **Accepted** means a deliberate decision not to act, with
the reason stated — the same discipline the product's own risk-acceptance feature enforces (Track
3.2.3).

| Severity | Total | Fixed | Open | Accepted |
|---|---|---|---|---|
| Critical | 3 | 3 | 0 | 0 |
| High | 7 | 7 | 0 | 0 |
| Medium | 19 | 18 | 0 | 1 |
| Low | 3 | 2 | 0 | 1 |
| Informational | 2 | 0 | 0 | 2 |
| **Total** | **34** | **30** | **0** | **4** |

**Open:** none.
**Accepted:** NR-2026-024, NR-2026-027, NR-2026-029, NR-2026-030.

Track 8 closed the five findings Track 7 left open — 008b, 017, 025, 028 and 032 — each with a
regression test named in its entry below. NR-2026-027 **stays accepted**, and honestly so: .NET has
no in-process sandbox, so a loaded plugin still runs with the API's full authority. What Track 8 added
there is the mitigation the entry proposed, publisher verification before load, which changes the
trust decision without confining anything. It is recorded as a mitigated acceptance rather than a
fix.

---

## Critical

### NR-2026-001 — Desktop SSO flow allows one-click account takeover
* **Severity:** Critical (likelihood: high — a link is all it takes; impact: full account compromise, any user)
* **Tier / file:** API · [`src/API/Controllers/AuthenticationController.cs`](../../src/API/Controllers/AuthenticationController.cs)
* **Milestone:** 7.3.1 / 7.3.2 · **Status:** Fixed
* **How established:** Read the three actions end to end and traced the state machine through
  `Model.Authentication.SAMLRequest`, whose `Status` property defaults to `"requested"` — which is
  the value `SAMLSingIn` requires before it will accept. Confirmed `SAMLRequest` and `AppSAMLToken`
  are both `[AllowAnonymous]`.
* **Exploitability:** `GET /Authentication/SAMLRequest?requestId=X` was anonymous and created a
  pending sign-in under **whatever `X` the caller chose**. `GET /Authentication/SAMLSingIn` then
  marked it `accepted` the moment the browser presented a valid SAML identity, with no consent step.
  `GET /Authentication/AppSAMLToken?requestId=X` — also anonymous, no cookie, no client binding —
  returned a full session JWT for that identity to anybody who asked, and left the cache entry in
  place for repeat collection.

  The attack therefore involved no guessing at all. An attacker picks `X`, sends a colleague
  `https://netrisk.example/Authentication/SAMLRequest?requestId=X`, and the victim's existing
  single-sign-on session silently completes the flow on one click (the `headerSelector` scheme
  forwards to `saml2` when there is no `Authorization` header, and the `SAMLReqID` cookie is
  `SameSite=None` so it survives the identity provider round trip). The attacker, polling
  `AppSAMLToken`, receives the victim's session token. Any account, including an administrator's.
  SAML was **enabled by default** in the shipped `appsettings.json`.
* **Fix, in four parts:**
  1. the request id is minted by the server (`GET /Authentication/SAMLRequestId`), 32 characters of
     CSPRNG output (~190 bits), and minting requires an **administrator-approved client
     registration** — so an anonymous outsider cannot create a pending sign-in at all;
  2. the browser endpoint refuses any id the server did not mint;
  3. `SAMLSingIn` no longer accepts on sight. It renders a consent page **naming the machine that
     asked** (from the client registration's hostname) and requires a POST carrying a single-use
     anti-forgery token that only appears inside that page — necessary because the SAML session
     cookie must be `SameSite=None`, so a cross-site form submission would otherwise carry it;
  4. redemption requires the same `ClientId` that minted the request and removes the cache entry
     before writing the token, so it is single-use even under a race.
* **Regression test:** [`API.Tests/Security/SamlSignInFlowTest.cs`](../../src/API.Tests/Security/SamlSignInFlowTest.cs) — 23 cases, including `TheBrowserEndpointRefusesAnIdTheServerDidNotMint`, `ReachingTheSignInPageDoesNotAcceptTheRequest`, `ApprovingWithoutTheRightTokenIsRefused`, `AnotherClientCannotCollectTheToken`, `TheTokenCanOnlyBeCollectedOnce`.
* **Note on the audit itself:** the first pass of this fix only shortened the guessing window
  (minimum id length plus single use). The repo's own `/security-review` gate caught that the id was
  still *caller-chosen*, which no amount of entropy addresses. The baseline report is
  [baseline-2026-08-26.md](baseline-2026-08-26.md); this is the gate earning its place.

### NR-2026-002 — Security tokens drawn from a non-cryptographic PRNG
* **Severity:** Critical (likelihood: medium — needs state recovery; impact: password-reset takeover of any account)
* **Tier / file:** Tools · [`src/Tools/RandomGenerator.cs`](../../src/Tools/RandomGenerator.cs)
* **Milestone:** 7.3.2 · **Status:** Fixed
* **How established:** Grepped every call site of `RandomGenerator` and `new Random(` across `src`
  and `libs`, then read each one to see whether its output reaches a user.
* **Exploitability:** One shared `private static readonly Random` produced the JWT signing key
  (`EnvironmentService.ServerSecretToken`), **password-reset link keys** (`LinksService.CreateLink`,
  40 chars), file and report access keys (`FilesService.Create`, `ReportsService`), generated user
  passwords (`UsersController`, `UserCommand`) and the SAML request id
  (`LoginViewModel`). .NET's parameterless `Random` is xoshiro256\*\* with 256 bits of state, and
  several of those values are handed to the requester **by design** — a reset link arrives by
  e-mail, a file key comes back in the upload response. An attacker who requests a handful of reset
  links for their own account, or uploads a few files, observes generator output and can recover the
  state, then predict *other people's* reset keys and file capabilities. CWE-338.
  `FaceIDService.GenerateRandomValidationSequenceAsync` used the same source for the liveness
  challenge, making the anti-replay sequence predictable.
* **Fix:** `RandomGenerator` draws from `RandomNumberGenerator.GetInt32` (rejection-sampled, so no
  modulo bias across the 65-symbol alphabet) and holds no state; a `RandomToken(byteCount)` helper
  was added for new callers that should say how much entropy they carry. `FaceIDService` uses
  `RandomNumberGenerator.GetInt32` directly.
* **Regression test:** [`Tools.Tests/Security/RandomGeneratorTest.cs`](../../src/Tools.Tests/Security/RandomGeneratorTest.cs) — `RandomGeneratorHoldsNoPseudoRandomState` reflects over the type and fails on the pre-fix code, which held a `Random` field.

### NR-2026-003 — Shipped configuration serves TLS with a private key committed to the repository
* **Severity:** Critical (likelihood: high — it is the default; impact: complete loss of transport security)
* **Tier / file:** API, WebSite · [`src/API/appsettings.json`](../../src/API/appsettings.json), [`src/WebSite/appsettings.json`](../../src/WebSite/appsettings.json)
* **Milestone:** 7.3.3 / 7.4.1 · **Status:** Fixed
* **How established:** `git ls-files` for key material, then `openssl` on each file.
* **Exploitability:** `src/API/Certificates/` and `src/WebSite/Certificates/` contain
  `certificate.pfx`, `key.pem`, `localhost.pfx`, `localhost.key` and `demowebapp.local.pfx` —
  self-signed, expired since 2023, **private keys included**. Having those for local development is
  unremarkable. What was not is that the shipped `appsettings.json` in both projects pointed at one
  of them with the password `"pass"`, in the very file that becomes the deployment template. An
  installation that deployed it unchanged served with a key held by everyone who has cloned the
  repository: passive decryption of every session, including the Basic-auth password on first
  sign-in, and trivial impersonation of the API.
* **Fix:** `Tools.Security.CommittedCertificates` recognises those file names and those placeholder
  passwords, and a **Release** build of either host *refuses to start* rather than warning. Warning
  was rejected deliberately: a start-up warning is read once and then lives in a log nobody tails,
  and the whole point of the finding is that the insecure configuration is the one you get by
  changing nothing. A Debug build permits it (that is what it is for), as does
  `Security:AllowDevelopmentCertificate=true`.
* **Regression test:** [`Tools.Tests/Security/CommittedCertificatesTest.cs`](../../src/Tools.Tests/Security/CommittedCertificatesTest.cs)
* **See also the committed-secret note at the end of this document.** The files themselves are left
  in history on purpose.

---

## High

### NR-2026-004 — Desktop client accepted any server certificate
* **Severity:** High (likelihood: medium — needs a network position; impact: full session compromise including the password)
* **Tier / file:** ClientServices · [`src/ClientServices/Services/RestService.cs`](../../src/ClientServices/Services/RestService.cs)
* **Milestone:** 7.4.1 · **Status:** Fixed
* **How established:** Grepped for `RemoteCertificateValidationCallback`,
  `ServerCertificateCustomValidationCallback` and `DangerousAcceptAnyServerCertificateValidator`
  across `src` and `libs`, then read each hit.
* **Exploitability:** `RemoteCertificateValidationCallback = (…) => true`, unconditionally, with its
  own `//TODO: Remove this line` beside it — on the client every desktop-to-API call goes through.
  Transport authentication was therefore absent: anything able to answer on the configured host and
  port (hostile DNS, captive portal, ARP spoof on a conference network) could read and rewrite the
  whole session, including the Basic-auth header on sign-in.
* **Fix:** `Tools.Security.ServerCertificatePolicy` returns `null` — meaning "use the platform's
  validation" — unless the installation has explicitly set `Server:AllowInvalidCertificate`, which
  logs a warning naming the setting on every start-up. The supported route for a private CA is the
  operating-system trust store, documented in [DATA_PROTECTION.md](DATA_PROTECTION.md).
* **Regression test:** [`Tools.Tests/Security/ServerCertificatePolicyTest.cs`](../../src/Tools.Tests/Security/ServerCertificatePolicyTest.cs)

### NR-2026-005 — First-run server verification accepted any certificate
* **Severity:** High (likelihood: medium; impact: the client is pinned to an attacker's server for its whole life)
* **Tier / file:** GUIClient · [`src/GUIClient/App.axaml.cs`](../../src/GUIClient/App.axaml.cs)
* **Milestone:** 7.4.1 · **Status:** Fixed
* **How established:** Same sweep as NR-2026-004.
* **Exploitability:** `VerifyServerUrlAsync` — the step that decides which server the client will
  trust from then on — set `ServerCertificateCustomValidationCallback = (…) => true`. So the
  trust-establishing step was itself unauthenticated, and a TLS failure was reported to the user as
  "Please enter a valid URL", indistinguishable from a typo.
* **Fix:** the same policy object, plus a distinct, fatal error message for a certificate failure —
  milestone 7.4.1's "must hard-fail with a clear error, never silently proceed".
* **Regression test:** as NR-2026-004 (shared policy); the GUI change is compile- and lint-verified
  only, because Avalonia.Native cannot start its render timer in this environment.

### NR-2026-006 — Arbitrary file write through the chunked-upload endpoints
* **Severity:** High (likelihood: high for any authenticated user; impact: arbitrary write as the API account)
* **Tier / file:** ServerServices · [`src/ServerServices/Services/FilesService.cs`](../../src/ServerServices/Services/FilesService.cs)
* **Milestone:** 7.3.1 · **Status:** Fixed
* **How established:** Read `FilesController.CreateLocalFileChunk` and followed `FileChunk.FileId`
  into `FilesService.SaveChunk`. Confirmed `GET /Files/local/id` merely *suggests* a GUID and the
  server never checks the value that comes back.
* **Exploitability:** `Path.Combine(_baseUploadPath, chunk.FileId)` with a caller-supplied
  `FileId`. `Path.Combine` is not a containment primitive: `"../../../.."` walks out of the first
  argument and a rooted second argument discards it entirely. `SaveChunk` then called
  `Directory.CreateDirectory` and `File.WriteAllBytes` on the result, so any authenticated user could
  create directories anywhere the API process could write and drop `{n}.part` files into them —
  and `local/complete` reassembled them into a `.dat` file of the caller's choosing. `DeleteChunks`
  gave arbitrary deletion of `{n}.part` files by the same route.
* **Fix:** `Tools.Security.SafePathTool` — a character allowlist (letters, digits, dash, underscore,
  dot; no leading dot; no `..`; ≤128 chars) *and* a resolved-path containment check with a trailing
  separator, so a symlink planted inside the staging directory is also caught. Applied to all five
  call sites (`SaveChunk`, `CombineChunks`, `DeleteChunks`, `CountChunks`,
  `CompleteChunkedUpload`), and the controller maps the rejection to 400 rather than 500. An
  allowlist rather than a blocklist because blocklists lose to encoding tricks.
* **Regression test:** [`ServerServices.Tests/Track7/FilesServiceUploadPathTest.cs`](../../src/ServerServices.Tests/Track7/FilesServiceUploadPathTest.cs), [`Tools.Tests/Security/SafePathToolTest.cs`](../../src/Tools.Tests/Security/SafePathToolTest.cs)

### NR-2026-007 — Disabled users could still authenticate with Basic auth
* **Severity:** High (likelihood: high — every offboarding; impact: continued access after deprovisioning)
* **Tier / file:** API · [`src/API/Security/BasicAuthenticationHandler.cs`](../../src/API/Security/BasicAuthenticationHandler.cs)
* **Milestone:** 7.3.1 · **Status:** Fixed
* **How established:** Compared the two credential handlers side by side. `JwtAuthenticationHandler`
  resolves the user through `FindEnabledActiveUserAsync`, which filters on `enabled == true && lockout == 0`.
  `BasicAuthenticationHandler` called `GetUserAsync` and checked only `Lockout`.
* **Exploitability:** `user.Enabled` is the flag the administrator UI sets and the flag SCIM
  deprovisioning writes (`ScimService` sets both `Enabled` and `Lockout`, which is why the gap was
  easy to miss — the common path happened to set both). A user disabled through any path that set
  only `Enabled` retained full access by presenting Basic credentials, which is also how the desktop
  client signs in before it holds a token.
* **Fix:** the handler refuses `user.Enabled != true`, logging the refusal.
* **Regression test:** covered by the handler's own path in `API.Tests`; the asymmetry is now
  impossible to reintroduce silently because both handlers are asserted in
  [`ASVS_L2_CHECKLIST.md`](ASVS_L2_CHECKLIST.md) §2.2.

### NR-2026-008 — No brute-force protection anywhere
* **Severity:** High (likelihood: high; impact: account compromise by guessing)
* **Tier / file:** API, ServerServices
* **Milestone:** 7.3.2 · **Status:** Fixed (with a documented residual, NR-2026-008b)
* **How established:** Grepped for `FailedLogin`, `failed_login`, `Lockout` across the solution.
  `BasicAuthenticationHandler` and `ApiTokenAuthenticationHandler` *read* `User.Lockout`; the only
  writers are the administrator UI and SCIM. Nothing counted a failure. This confirms the Track 6
  inventory's note that `failed_login_attempts` carried no live logic.
* **Exploitability:** A password could be guessed as fast as bcrypt would answer, indefinitely, with
  no lockout, no delay, no rate limit and no audit event.
* **Fix:** `ServerServices.Security.LoginAttemptTracker` — four free failures per identity, then a
  lockout doubling from 5 s to a 15-minute cap, counters decaying after 30 minutes of quiet, keyed on
  **both** the account and the source address (account-only lets an attacker lock a colleague out;
  address-only lets a distributed attempt through). Doubling from a short base rather than a flat
  "five strikes, fifteen minutes", because a flat lockout is itself a denial-of-service primitive
  against anyone whose login you know. Plus `API.Security.AuthRateLimiting`, a per-source fixed
  window on the credential paths, which exists for a different reason: bcrypt at cost 15 is
  deliberately expensive, so a few hundred concurrent *refused* attempts are a problem on their own.
* **Regression test:** [`ServerServices.Tests/Track7/LoginAttemptTrackerTest.cs`](../../src/ServerServices.Tests/Track7/LoginAttemptTrackerTest.cs) — 12 cases.

### NR-2026-010 — SAML assertion signature verification disabled in the shipped configuration
* **Severity:** High (likelihood: medium — requires SAML enabled, which was the default; impact: authentication as any user)
* **Tier / file:** API · [`src/API/appsettings.json`](../../src/API/appsettings.json), [`build/puppet/.../api/appsettings.json.epp`](../../build/puppet/modules/netrisk/templates/api/appsettings.json.epp)
* **Milestone:** 7.3.2 · **Status:** Fixed
* **How established:** Read the shipped configuration, confirmed the property exists on the
  `UOX.Saml2.Authentication` 4.2.0 assembly (`get_/set_OmitAssertionSignatureCheck`), and confirmed
  that `Saml2:Enabled` was `true` in the same file — pointing at the *public* test identity provider
  `stubidp.sustainsys.com`.
* **Exploitability:** `"OmitAssertionSignatureCheck": true` instructs the SAML library not to verify
  the identity provider's signature on the assertion. An assertion is then just XML: forge one
  naming any user and the API accepts it. The Puppet production template carried the same value,
  plus `HashingAlgorithm`/`DigestAlgorithm` of `SHA1`.
* **Fix:** `OmitAssertionSignatureCheck` is `false` in both the shipped configuration and the Puppet
  template; the Puppet template's algorithms moved to SHA-256; and `Saml2:Enabled` now defaults to
  **false**, because an enterprise authentication path enabled by default against a public test
  identity provider is a footgun independent of the signature setting.
* **Verification:** configuration change; the surrounding assertion-validation code
  (`ServerServices/Auth/SamlAssertion.cs`, used by the Track 4 provider path) already verifies
  signatures and prohibits DTDs, which `ServerServices.Tests/Track4/SamlAssertionTest.cs` asserts —
  including `DtdProcessingIsProhibitedSoAResponseCannotBeAnXxe`.

### NR-2026-011 — Stored integration credentials encrypted with a constant IV and no authentication
* **Severity:** High (likelihood: low for recovery, high for the equality leak; impact: disclosure and undetected tampering of third-party credentials)
* **Tier / file:** ServerServices, Tools · [`src/ServerServices/Security/SecretProtector.cs`](../../src/ServerServices/Security/SecretProtector.cs), [`src/Tools/Criptography/AES.cs`](../../src/Tools/Criptography/AES.cs)
* **Milestone:** 7.4.2 · **Status:** Fixed
* **How established:** Read `AES.Encrypt`/`Decrypt`: `key = SHA256(passphrase)`,
  `IV = MD5(passphrase)`. Both are pure functions of the passphrase, so both are constant per
  installation.
* **Exploitability:** Three defects in one construction. A constant IV in CBC means identical
  plaintexts produce byte-identical ciphertexts — anyone with read access to the table learns which
  connections share a token without decrypting anything, and it is the precondition for several
  classical CBC attacks. No salt means one precomputation covers every installation that shares a
  passphrase. And CBC without a MAC cannot distinguish a tampered ciphertext from a valid one:
  decryption with a wrong key does not reliably fail, it can return plausible garbage, which is also
  what made the "encrypted on another installation" error path unreliable.
* **Fix:** `Tools.Criptography.AesGcm256` — AES-256-GCM, a fresh 16-byte salt and 12-byte nonce per
  message, HKDF-SHA256 (not PBKDF2: the input is already a 256-bit installation key, so iterations
  would buy nothing while adding hundreds of milliseconds to every notification dispatch), and the
  full 128-bit tag. `SecretProtector` writes `enc:v2:` and still reads `enc:v1:`, upgrading a v1
  value in place on save — but only after re-encrypting the decrypted value under v1 and comparing,
  because v1 is deterministic and that round-trip is the only way to tell "decrypted correctly" from
  "decrypted to garbage". A value that fails the check is left byte-identical, so a credential
  encrypted on another installation stays recoverable there.
* **Regression test:** [`Tools.Tests/Security/AesGcm256Test.cs`](../../src/Tools.Tests/Security/AesGcm256Test.cs) (`EncryptingTheSameValueTwiceProducesDifferentCiphertext`, `TamperingWithTheCiphertextIsDetected`), [`ServerServices.Tests/Track4/SecretProtectorTest.cs`](../../src/ServerServices.Tests/Track4/SecretProtectorTest.cs) (round-trip, v1 read, in-place upgrade, foreign value untouched).

---

## Medium

### NR-2026-009 — WebAuthn enrolment endpoints carried no authorization metadata
* **Severity:** Medium (likelihood: low — fails closed incidentally; impact: would be authenticator enrolment for another account)
* **Tier / file:** API · [`src/API/Controllers/WebAuthnController.cs`](../../src/API/Controllers/WebAuthnController.cs)
* **Milestone:** 7.3.1 · **Status:** Fixed
* **How established:** Enumerated every controller's class- and action-level attributes rather than
  reading the doc comments. `WebAuthnController` had `[ApiController]` and `[Route]` and no
  `[Authorize]`, while its own summary said "The registration endpoints are authenticated".
* **Exploitability:** `credentials` (GET), `register/begin`, `register/complete` and `status` had no
  authorization attribute on the action or the class. They failed closed in practice, but only
  incidentally: `ApiBaseController.GetUser()` throws `UserNotFoundException` when there is no
  principal, which surfaced as a 500 rather than a 401. That is not an access control — it is one
  refactor away from being an open enrolment endpoint, and enrolling an authenticator for somebody
  else's account is exactly what an attacker wants.
* **Fix:** class-level `[Authorize(Policy = "RequireValidUser")]`, and — more importantly — a
  reflective inventory test so the *next* one is caught.
* **Regression test:** [`API.Tests/Security/ControllerAuthorizationInventoryTest.cs`](../../src/API.Tests/Security/ControllerAuthorizationInventoryTest.cs) — every action must be authorized or on a justified anonymous allowlist; the allowlist must have no stale entries; every entry needs a stated reason; and the fallback policy must require an authenticated, existing user.
* **Note:** the sweep also confirmed the good news — `PermissionPolicyProvider` delegates
  `GetFallbackPolicyAsync` to a policy that requires an authenticated, existing user, so an
  unannotated endpoint is *denied* rather than open. That was verified, not assumed.

### NR-2026-012 — Session tokens: no issuer/audience validation, 24-hour lifetime, no revocation
* **Severity:** Medium (likelihood: low; impact: a stolen token stays valid for a day, and survives a password change)
* **Tier / file:** API · [`src/API/AuthenticationBootstrapper.cs`](../../src/API/AuthenticationBootstrapper.cs), [`src/API/Security/JwtAuthenticationHandler.cs`](../../src/API/Security/JwtAuthenticationHandler.cs)
* **Milestone:** 7.3.2 · **Status:** Fixed
* **How established:** Read the `TokenValidationParameters` (`ValidateIssuer = false`,
  `ValidateAudience = false`), the shipped `JWT:Timeout` of 1440 minutes, and searched for any writer
  of `LastPasswordChangeDate` — there was none.
* **Exploitability:** The only property a presented token had to satisfy was "signed with this
  installation's key", so any token minted under that key for any purpose would be accepted as a user
  session. The lifetime was a day against OWASP's minutes-not-hours guidance, with no refresh flow
  and nothing able to revoke early: changing a password — precisely the reaction to a suspected
  compromise — left every previously minted token working until it expired.
* **Fix:** issuer, audience and algorithm are pinned on both the minting and the validating side from
  a shared `JwtDefaults`, clock skew tightened to 30 s, `jti`/`iat`/`nbf` added, the default lifetime
  reduced to 60 minutes with a 1440-minute ceiling enforced (a longer configured value is clamped and
  logged, because it is a mistake rather than a policy), and real revocation: `ChangePassword` now
  writes `LastPasswordChangeDate` and the handler refuses any token whose `iat` predates it. One
  write invalidates every outstanding session for that account, using a column that already exists.
* **Verification:** covered by the existing `API.Tests` authentication suite; the revocation
  comparison tolerates 30 s of clock difference so that the token handed back by a password-change
  flow is not itself rejected.

### NR-2026-013 — No SSRF guard on outbound integration calls
* **Severity:** Medium (likelihood: low — needs the `configuration` permission; impact: cloud credential theft)
* **Tier / file:** ServerServices · [`src/ServerServices/Http/OutboundHttpClient.cs`](../../src/ServerServices/Http/OutboundHttpClient.cs)
* **Milestone:** 7.4.1 · **Status:** Fixed
* **How established:** Read every outbound HTTP construction and traced the URL back to its
  configuration source.
* **Exploitability:** Every Track 4 integration sends a request to a URL an administrator typed — a
  Slack webhook, a Jira base URL, a posture-provider endpoint — and the **response body comes back to
  the caller**, so this is not a blind SSRF. The interesting targets are not on the internet: the
  cloud instance metadata service at `169.254.169.254` (on a default IMDSv1 instance the response is
  a set of cloud credentials), the Kubernetes API on the node, an unauthenticated admin port on
  loopback.
* **Fix:** `ServerServices.Http.OutboundUrlPolicy`, evaluated in `OutboundHttpClient.SendAsync` so
  no provider can be written without it. Non-`http(s)` schemes always refused; link-local
  (`169.254.0.0/16`, `fe80::/10`) and the documented IPv6 metadata addresses always refused;
  private and loopback ranges allowed **by default**, because refusing them would break the
  on-premise deployments this product exists for, and refusable with
  `Integrations:BlockPrivateNetworks` plus an `Integrations:AllowedPrivateHosts` escape hatch that
  cannot override the metadata block. Resolution happens at send time and every resolved address is
  checked, so a DNS name pointing at the metadata address is caught; `AllowAutoRedirect = false`
  means a 302 cannot bypass it. A hostname that fails to resolve is allowed through deliberately, so
  a resolver hiccup surfaces as the real transport error rather than as "blocked for security
  reasons".
* **Regression test:** [`ServerServices.Tests/Track7/OutboundUrlPolicyTest.cs`](../../src/ServerServices.Tests/Track7/OutboundUrlPolicyTest.cs) — 38 cases including IPv4-mapped IPv6 literals, the classic bypass for an IPv4-only check.

### NR-2026-014 — Password-reset links indexed by MD5
* **Severity:** Medium (likelihood: very low; impact: reset-link collision)
* **Tier / file:** ServerServices · [`src/ServerServices/Services/LinksService.cs`](../../src/ServerServices/Services/LinksService.cs)
* **Milestone:** 7.4.2 · **Status:** Fixed
* **How established:** Read `CreateLink`/`LinkExists`/`GetLinkData`/`DeleteLink`.
* **Exploitability:** The key itself carries the security (40 characters of CSPRNG output after
  NR-2026-002), and finding a *second preimage* for MD5 remains infeasible, so this is not directly
  exploitable. It is in the register because a collision-broken digest guarding password-reset links
  is not something a security product should ship, and because the pre-fix combination — MD5 index
  over a *predictable* key — was a genuinely weak pair.
* **Fix:** new links are indexed by SHA-256; the MD5 lookup is kept as a fallback so links already
  issued when an installation upgrades still resolve, and it disappears on its own because
  `CleanLinks` deletes every expired link. `key_hash` is `varchar(255)`, so the wider digest needed
  no schema change.
* **The half that nearly went wrong.** The API stores the hash; the row is then pushed to the
  **WebSite** verbatim over `/sync`, and the WebSite hashes the key from the visitor's URL to look it
  up. Changing one side and not the other would have made every reset link fail to resolve —
  presenting as an expired link, with nothing logged. The digest choice therefore lives in one shared
  `Tools.Security.LinkKeyHash` that both sides call, so they cannot drift. See R4 under
  "Regressions introduced by this track's own fixes".
* **Regression test:** [`Tools.Tests/Security/LinkKeyHashTest.cs`](../../src/Tools.Tests/Security/LinkKeyHashTest.cs), plus `LinksService`'s existing in-memory service tests, which exercise create → resolve → delete through the new path.

### NR-2026-015 — No security response headers on either HTTP surface
* **Severity:** Medium (likelihood: medium; impact: clickjacking, MIME confusion, referrer leakage)
* **Tier / file:** API, WebSite
* **Milestone:** 7.4.3 · **Status:** Fixed
* **How established:** Read both `Program.cs` files end to end. The API set none. The WebSite called
  `UseHsts()` in non-development and nothing else.
* **Exploitability:** The API is consumed by a desktop client rather than a browser, which is why it
  was easy to overlook — but its JSON and its error bodies render perfectly well in a browser, and
  the public WebSite serves pages and installer downloads.
* **Fix:** `Tools.Security.SecurityHeaderPolicy` holds the policy as data — which headers, which
  values, and a comment per header saying what it is for — with a thin middleware in each host. Not
  one shared middleware, because that would put a `Microsoft.AspNetCore.App` framework reference into
  a library the Avalonia desktop client also consumes. HSTS (180 days by default, `0` disables it,
  which is the correct setting while an installation is still on a self-signed certificate — pinning
  a browser to HTTPS for a host whose certificate does not validate is not recoverable from the
  server side), `nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`,
  `X-Permitted-Cross-Domain-Policies: none`, `Cross-Origin-Resource-Policy: same-origin`, a CSP
  (`default-src 'none'` on the API, a page policy on the WebSite that keeps `script-src 'self'` with
  no inline allowance), and removal of `Server`.
* **Regression test:** [`Tools.Tests/Security/SecurityHeaderPolicyTest.cs`](../../src/Tools.Tests/Security/SecurityHeaderPolicyTest.cs)

### NR-2026-016 — SAML session cookie sent over plain HTTP
* **Severity:** Medium (likelihood: low; impact: session cookie disclosure)
* **Tier / file:** API · [`src/API/AuthenticationBootstrapper.cs`](../../src/API/AuthenticationBootstrapper.cs)
* **Milestone:** 7.4.3 · **Status:** Fixed
* **How established:** Read the cookie options: `SecurePolicy = CookieSecurePolicy.SameAsRequest`.
* **Exploitability:** "Same as request" means a plain-HTTP hop ships the SAML session cookie in
  clear. `SameSite=None` is genuinely required here — the cookie has to survive the identity
  provider's cross-site POST back — which is exactly why the rest had to be tight.
* **Fix:** `CookieSecurePolicy.Always` and `IsEssential`. The `SameSite=None` requirement is now the
  documented reason the approval form in NR-2026-001 carries its own anti-forgery token.

### NR-2026-018 — Malformed Basic auth header produced a 500
* **Severity:** Medium (likelihood: high; impact: information disclosure through error differentiation, and truncated passwords)
* **Tier / file:** API · [`src/API/Security/BasicAuthenticationHandler.cs`](../../src/API/Security/BasicAuthenticationHandler.cs)
* **Milestone:** 7.3.1 · **Status:** Fixed
* **How established:** Read the decode path: `Convert.FromBase64String(token)` then
  `credentialstring.Split(':')` then `credentials[1]`.
* **Exploitability:** A header that is not valid base64 throws `FormatException`; one with no colon
  throws `IndexOutOfRangeException`. Either surfaced as a 500 from an unauthenticated request, which
  lets a caller distinguish server states by malforming a header. Separately, splitting on *every*
  colon silently truncated any password containing one — RFC 7617 forbids a colon in the user-id but
  not in the password.
* **Fix:** `Convert.TryFromBase64String` and a split on the *first* colon only, both in a
  `TryReadCredentials` helper; every failure path routes through one `Unauthenticated` method so no
  path can set the status code without the `WWW-Authenticate` header.

### NR-2026-019 — Webhook URL secret compared in non-constant time
* **Severity:** Medium (likelihood: low — network timing is noisy; impact: forged inbound webhook)
* **Tier / file:** ServerServices · [`src/ServerServices/Integrations/IssueTrackers/IssueTrackerService.cs`](../../src/ServerServices/Integrations/IssueTrackers/IssueTrackerService.cs)
* **Milestone:** 7.4.2 · **Status:** Fixed
* **How established:** Read the webhook path for all four providers. GitHub and GitLab already used
  `CryptographicOperations.FixedTimeEquals` on their HMAC. Jira and Azure DevOps do not sign, so they
  rely on a shared URL secret — and that was compared with `!=`.
* **Exploitability:** `!=` on a string returns as soon as two characters differ, leaking the secret
  one character at a time to a caller who can measure the response. The signed providers were already
  correct; the unsigned ones, which rely on this secret *alone*, were not.
* **Fix:** a `FixedTimeEquals` helper over the UTF-8 bytes.

### NR-2026-020 — Uploads staged in a world-writable directory
* **Severity:** Medium (likelihood: medium on a shared host; impact: disclosure of uploaded scan reports, symlink swap)
* **Tier / file:** ServerServices · [`src/ServerServices/Services/FilesService.cs`](../../src/ServerServices/Services/FilesService.cs)
* **Milestone:** 7.4.2 · **Status:** Fixed
* **How established:** Read the constructor: `/tmp/netrisk-api` on Linux and macOS.
* **Exploitability:** Uploaded scan reports — asset A1, the highest-value data in the product —
  were staged in a directory every local account can read and write, under predictable names. That
  invites both disclosure and a symlink swap between the chunk write and the reassembly.
* **Fix:** `/var/netrisk/netrisk-api/uploads` on Linux (mirroring where `EnvironmentService` already
  keeps the installation key) and the application-data folder on macOS, with `0700` applied on Unix.
  If the preferred directory cannot be created — a packaged install that does not own it yet — the
  service falls back to the temporary directory *and warns that it is world-writable*, rather than
  failing to accept uploads.

### NR-2026-021 — Schema name interpolated into `information_schema` queries
* **Severity:** Low→Medium (likelihood: very low; impact: SQL injection if the premise ever changes)
* **Tier / file:** ServerServices · [`src/ServerServices/Services/DatabaseService.cs`](../../src/ServerServices/Services/DatabaseService.cs)
* **Milestone:** 7.1.2 · **Status:** Fixed
* **How established:** Grepped every `MySqlCommand` construction in `src`. Three used
  `$"… table_schema = '{schema}' …"`.
* **Exploitability:** `schema` is `connection.Database`, i.e. the database name from the connection
  string — operator configuration, not request input — so this was not reachable from a request. It
  is in the register because "not currently attacker-controlled" is the kind of premise that quietly
  stops being true, and parameterising cost nothing.
* **Fix:** all three parameterised. The remaining raw SQL in the codebase is the numbered-SQL upgrade
  machinery, whose statements come from reviewed files on disk and whose only interpolations are
  identifiers from `SchemaUpgradePhases.yaml`; that is covered by
  `ConsoleClient.Tests/DB/SchemaUpgradeIdempotenceTest` and the Track 6 integration suite.

### NR-2026-022 — Legacy Nessus parse path allowed DTD processing
* **Severity:** Low→Medium (likelihood: none today — unreachable; impact: entity-expansion denial of service)
* **Tier / file:** ServerServices, libs · [`src/ServerServices/Services/Importers/NessusImporter.cs`](../../src/ServerServices/Services/Importers/NessusImporter.cs)
* **Milestone:** 7.1.2 · **Status:** Fixed
* **How established:** Traced every XML entry point. The contract importers
  (`NessusReportImporter`, `OpenVasImporter`, `BurpImporter`) all set `DtdProcessing.Prohibit` and
  `XmlResolver = null`, **and that is now proved by a test rather than read from a comment**. The
  legacy `NessusImporter` called `NessusClientData_v2.ParseAsync`, which hands a plain `StreamReader`
  to `XmlSerializer`; that builds an `XmlTextReader` whose `DtdProcessing` defaults to `Parse` with no
  entity-expansion budget. Confirmed unreachable: nothing in `src` calls
  `IVulnerabilityImporterFactory.GetImporter`.
* **Exploitability:** No external-entity file read (that path does null the resolver), but nested
  internal entities — "billion laughs" — could consume the process's memory. Unreachable today.
* **Fix:** hardened at the call site in this repository (the parser is in the `libs/NessusParser`
  submodule, a separate repository), rather than deleted, because "unreachable today" is not a
  property that stays true on its own.
* **Regression test:** [`ServerServices.Tests/Track7/ImporterXxeTest.cs`](../../src/ServerServices.Tests/Track7/ImporterXxeTest.cs) — external file entity, external HTTP entity and nested internal entities, against each of the three live importers.

### NR-2026-023 — Argument injection opening a scan-report URL
* **Severity:** Medium (likelihood: medium — the URL comes from an imported scan file; impact: arbitrary application launch on the analyst's workstation)
* **Tier / file:** GUIClient · [`src/GUIClient/ViewModels/VulnerabilitiesViewModel.cs`](../../src/GUIClient/ViewModels/VulnerabilitiesViewModel.cs)
* **Milestone:** 7.1.2 · **Status:** Fixed
* **How established:** Grepped `Process.Start` across `src` and read each call site, then traced the
  URL back to `Vulnerability` fields populated by the importers.
* **Exploitability:** The URL is attacker-influenced — whoever produced the `.nessus` file chose it.
  On macOS the call was `Process.Start("open", "-u " + url)`; the second parameter is one string the
  operating system re-splits, so a URL containing a space smuggles further arguments to `open`,
  `-a SomeApplication` among them. On Windows, `UseShellExecute = true` with an arbitrary `FileName`
  launches a local path or an executable as readily as it opens a link. On any platform a non-web
  scheme (`file:`, `smb:`, a registered custom scheme) is handled by whatever claimed it.
* **Fix:** `Tools.Security.ExternalUrlPolicy` — absolute URL, scheme `http`/`https`, no whitespace or
  control characters (rejected *before* `Uri.TryCreate`, which tolerates surrounding whitespace),
  length-bounded — and the launcher arguments now go through `ArgumentList` so the runtime quotes
  each one instead of handing the OS a string to re-parse. Applied to both `Process.Start` sites.
* **Regression test:** [`Tools.Tests/Security/ExternalUrlPolicyTest.cs`](../../src/Tools.Tests/Security/ExternalUrlPolicyTest.cs)

### NR-2026-026 — Website sync could disable certificate validation silently
* **Severity:** Low→Medium (likelihood: low; impact: disclosure of the pushed register in transit)
* **Tier / file:** ServerServices · [`src/ServerServices/Services/SyncClient.cs`](../../src/ServerServices/Services/SyncClient.cs)
* **Milestone:** 7.4.1 · **Status:** Fixed
* **How established:** Read `CreateClient(url, insecure)` and traced `insecure` back to
  `WebsiteSyncSettings` and the `--insecure` console flag.
* **Exploitability:** Unlike NR-2026-004 this was always an explicit opt-in flag, which is the right
  shape. What was missing is that it was *silent*: an operator who set it during setup had no
  reminder that every push of the risk register was now interceptable. Payloads are Ed25519-signed,
  so they cannot be forged — but they are readable.
* **Fix:** one loud warning per process naming the host and the remedy.

### NR-2026-033 — Production secrets could only be supplied through a file on disk
* **Severity:** Medium (likelihood: certain — it was the only option; impact: credentials on disk in a file that deployment tooling templates and copies)
* **Tier / file:** API, WebSite, BackgroundJobs, ConsoleClient · `Program.cs` in each
* **Milestone:** 7.3.3 · **Status:** Fixed
* **How established:** Attempting to *verify* the guidance being written into
  [SECRETS.md](SECRETS.md) — "production uses environment variables" — by starting the WebSite with
  `LocalDb__ConnectionString` set. It was ignored. Grepping for `AddEnvironmentVariables` across the
  four hosts returned nothing.
* **Exploitability:** Not an exploit in itself. It is the *root cause* of NR-2026-025 and it made
  §7.3.3's core requirement unachievable: with no environment provider registered, the only place an
  operator could put a database password or a certificate password was `appsettings.json` on the
  target host — which is exactly what the milestone forbids, and exactly why the Puppet templates
  render the password to disk. The documented user-secrets and environment workflow, in CLAUDE.md and
  in the deployment guides, did not work.

  A second defect in the same builder: `AddJsonFile` was registered **after** `AddUserSecrets`. Later
  providers win in .NET configuration, so the committed `appsettings.json` overrode every key a
  developer set in user-secrets — the opposite of what an override means, and the kind of thing only
  noticed when a value silently does nothing.
* **Fix:** all four hosts now build configuration as **file → user-secrets (Debug only) →
  environment**, which is both the conventional precedence and the one the documentation assumed.
  `Database__ConnectionString`, `https__certificate__password` and every other key now work from the
  environment with no other change.
* **Verification:** empirical, not just a compile — the WebSite was started with
  `LocalDb__ConnectionString` pointing at a scratch path and created its SQLite database there,
  proving the environment value overrode the committed `appsettings.json`. Recorded in
  [baseline-2026-08-26.md](baseline-2026-08-26.md) §4.
* **Note:** this finding exists because a documentation claim was tested instead of asserted. It is
  the same failure mode as the two historical defects named at the top of this register, caught the
  same way.

---

## Low and informational

### NR-2026-024 — No CORS middleware on the API *(Informational, no action)*
No `AddCors`/`UseCors` anywhere, which is the **secure** default: with no CORS policy a browser
refuses to expose a cross-origin response, so a hostile page cannot read the API even with the user's
cookies. Recorded so that a future "let's add CORS for a web client" change is understood as a
security decision, and so §7.4.3's "never `AllowAnyOrigin` in production" has something to point at.
Established by grep.

### NR-2026-029 — `AllowedHosts: "*"` *(Low, risk-accepted)*
Both hosts ship `"AllowedHosts": "*"`, so the host-filtering middleware accepts any `Host` header.
The concrete risk is cache-poisoning or password-reset-link poisoning via the `Host` header — but
NetRisk builds reset links from `website:protocol/host/port` configuration, not from the request, so
the usual exploit does not apply. Accepted with a documented recommendation to set an explicit host
list in production ([SECRETS.md](SECRETS.md) § deployment checklist). Established by reading
`appsettings.json` and confirming `LinksService.CreateLink` uses configuration.

### NR-2026-030 — `EnableSensitiveDataLogging` present *(Informational, no action)*
`DalService` enables EF sensitive-data logging, but inside `#if DEBUG` **and** behind
`Database:EnableSQLLogging`. A Release binary cannot turn it on. Verified by reading the
preprocessor guard.

### NR-2026-031 — No SBOM shipped with artifacts *(Low, fixed)*
Releases carried no bill of materials, so a consumer could not tell what was inside a binary or be
notified of a new CVE against a released version. Fixed by milestone 7.2.2: `build/Build.Sbom.cs`
emits `netrisk-<component>-<version>.cdx.json` plus a `.sha256` beside every packaged component,
generated at build time from the resolved dependency graph. Covered by
[`Packaging.Tests/SbomTest.cs`](../../src/Packaging.Tests/SbomTest.cs).

---

## Closed in Track 8

The five findings Track 7 left open, and the acceptance it left with a proposed mitigation. Each entry
keeps the original statement of what was open, so the record shows what was decided rather than only
what the code now does.

### NR-2026-008b — Brute-force counters are per process and in memory *(Medium, fixed in Track 8)*
* **Tier:** ServerServices, DAL · **Milestone:** 7.3.2 → closed in Track 8
* **What was open:** `LoginAttemptTracker` held its state in a `ConcurrentDictionary`, so counters
  reset on restart and were not shared between API instances behind a load balancer. An attacker who
  could spread attempts across instances got the per-instance budget on each.
* **Fix:** `ServerServices.Security.PersistedLoginAttemptTracker` over a `login_attempts` table keyed
  on `(identity, source)` — unique index `uq_login_attempts_identity_source`, db_version 82. The
  in-memory tracker's policy is unchanged (four free failures, doubling lockout from 5 s to a
  15-minute cap, decay after 30 minutes of quiet, keyed on account *and* source); only where the
  counter lives changed. Registered by `AddPersistedLoginThrottling()`.
  * The identity is lower-cased before it is keyed, so `Alice`, `alice` and `ALICE` share one budget.
    Without that, case variation is a free multiplier on the attempt limit.
  * An attempt with no identity is not persisted at all. Otherwise an unauthenticated flood writes a
    row per request, which is a self-inflicted amplification on exactly the request being flooded.
* **Regression test:** [`ServerServices.Tests/Track8/DeferredSecurityFixesInMemoryTest.cs`](../../src/ServerServices.Tests/Track8/DeferredSecurityFixesInMemoryTest.cs)
  — `TestFailuresAreCountedInTheDatabaseSoEveryInstanceSeesThem`,
  `TestASuccessfulLoginClearsTheSharedCounter`, `TestTheCounterIsKeyedOnIdentityAndSourceTogether`,
  `TestAnAttemptWithNoIdentityIsNotPersisted`,
  `TestTheIdentityIsLowerCasedSoCaseVariationsShareABudget`. The table itself is asserted by
  [`DAL.IntegrationTests/Track8GovernanceSchemaTests.cs`](../../src/DAL.IntegrationTests/Track8GovernanceSchemaTests.cs)
  `Phase13_TheRevocationListAndLockoutCounterAreUniquelyKeyed`, against a real MariaDB.
* **Residual, stated:** the counter is now shared, but it is a database write on a refused login. The
  per-source fixed window in `API.Security.AuthRateLimiting` is what keeps that from being a lever —
  it refuses before the write. An installation behind a load balancer with the rate limiter disabled
  has traded one problem for another, which is why the limiter is not optional in the shipped
  configuration.

### NR-2026-017 — No per-file access control on attachments *(Medium, fixed in Track 8)*
* **Tier:** ServerServices, API · [`src/ServerServices/Security/FileAccessAuthorizer.cs`](../../src/ServerServices/Security/FileAccessAuthorizer.cs)
* **What was open:** any authenticated user who knew a file's `unique_name` could download it through
  `GET /Files/{name}` regardless of which risk, mitigation or entity it belonged to, and
  `GET /Files/id/{id}` was enumerable by integer.
* **Fix, in two parts.**
  1. `nr_files` gained an `entity_id` (db_version 82), backfilled from the parent risk's entity, and
     came under the Track 2.3 model-level query filter — which closes cross-tenant reads for every
     query, not only the ones somebody remembered to guard. A file whose parent carries no entity
     keeps a NULL and stays visible: that is the honest outcome rather than a guess, and it is
     asserted rather than assumed.
  2. `IFileAccessAuthorizer.EnsureCanReadAsync` resolves whichever parent FK is set and applies that
     parent's permission rules. It is its own service, not a method on `IFilesService`, because the
     files service is also consumed by background jobs and the report renderer, which legitimately
     read attachments with no user in hand — folding the check into every read would have meant
     giving those callers a bypass parameter, which is how a control ends up being passed `false`
     everywhere.
* **Regression tests:**
  [`ServerServices.Tests/Track8/DeferredSecurityFixesInMemoryTest.cs`](../../src/ServerServices.Tests/Track8/DeferredSecurityFixesInMemoryTest.cs)
  for the rules — `TestAParentlessFileIsRefusedToEveryoneElse`,
  `TestARiskAttachmentIsRefusedWithoutTheRiskPermission`,
  `TestARiskAttachmentIsReadableByTheRiskOwnerWithoutTheBlanketPermission`,
  `TestAMitigationAttachmentInheritsTheRisksRules`,
  `TestAnAttachmentOfAnotherEntityIsInvisibleToAScopedCaller`, plus
  `TestTheUploaderCanAlwaysReadBackTheirOwnUpload` and `TestAnAdministratorCanReadAnything` for the
  two paths that must *not* be broken; and
  [`API.Tests/APITests/FileAccessControlTest.cs`](../../src/API.Tests/APITests/FileAccessControlTest.cs)
  for the endpoint — that **both** read routes consult the authorizer, that they consult it *before*
  the download is logged (otherwise the log records refused reads as reads that happened), and that a
  missing file and a refused file return the same status, so the route is not an enumeration oracle.
  The backfill is asserted against a real MariaDB by
  `Track8GovernanceSchemaTests.Phase13_BackfillsAttachmentEntitiesFromTheirParentRisk`.
* **Retained mitigation:** the unique name is still a SHA-256 of a 256-bit CSPRNG token rather than a
  hash of the file name, so the capability that route relies on remains genuinely unguessable.

### NR-2026-025 — Deployment templates write the database password to disk *(Medium, fixed in Track 8)*
* **Tier:** build · [`build/puppet/modules/netrisk/templates/env/netrisk.env.epp`](../../build/puppet/modules/netrisk/templates/env/netrisk.env.epp)
* **What was open:** four Puppet `appsettings.json.epp` templates rendered
  `Database:ConnectionString` with `pwd=` onto every target host. §7.3.3 forbids it, and the reason is
  mundane rather than exotic — a configuration file is the thing that gets copied into a support
  ticket and read by anything that can read the install directory.
* **Fix:** the credential moves to `/netrisk/netrisk.env`, rendered from a new template as
  `Database__ConnectionString` (the double underscore is .NET's configuration separator, so it sets
  `Database:ConnectionString` and takes precedence over the file). Puppet writes it `mode 0600`,
  owned by the service account, with `show_diff => false` — the Puppet run report is otherwise a
  second plaintext copy, published to whoever reads reports. The four `appsettings.json` templates now
  render a comment in place of the key, and the five `db_*` parameters were removed from them
  entirely: a declared-but-unused `$db_password` still hands the credential to the renderer and is
  the first thing the next person editing the file will reach for.
* **Container path:** each `build/Docker/entrypoint-*.sh` loads the file with `load_netrisk_env`,
  which reads it line by line and exports the raw remainder of each line. For the three services that
  drop privileges the function is carried across with `declare -f` and the file is read *inside* the
  `sudo -u netrisk` shell, because sudo scrubs the environment by default and a variable exported by
  root would not survive the switch.
* **Regression this fix caused, and how it is now prevented:** 2.17.0 read the file with
  `. /netrisk/netrisk.env`. The file is a literal `KEY=VALUE` environment file — Docker's
  `--env-file` format — and the value is a connection string full of `;`, which the shell reads as a
  command separator, so `Database__ConnectionString` became `server=<host>` and `port=4306`, `uid=…`
  and the rest were run as unrelated assignments. MySqlConnector fell back to its default port 3306,
  `API.Tools.SelfTest.ExecuteDBTests` timed out after 15 s, and every host restart-looped
  (`apldc1vds0044`, 2026-08-28: 220 `Connect Timeout expired` in 24 h, zero successful connections).
  `Packaging.Tests/DeploymentEnvironmentFileLoaderTest` now renders the real template — with a
  password made of shell metacharacters — and runs the real entrypoint loader over the result, and
  separately pins the truncation that `.` produces so nobody reintroduces it.
* **Regression test:** [`Packaging.Tests/DeploymentSecretPlacementTest.cs`](../../src/Packaging.Tests/DeploymentSecretPlacementTest.cs)
  — 14 cases over the shipped templates, manifests and entrypoints. It fails on the pre-fix
  templates, which rendered
  `"ConnectionString": "server=…;pwd=<%= $db_password -%>;…"`. Asserted on the templates rather than
  on a rendered host because Puppet cannot be run here, and a template that silently regains a
  `pwd=` is exactly the regression nobody notices.
* **Not covered:** whether a given operator's existing deployment is migrated. An installation that
  upgrades in place keeps whatever is already in its `appsettings.json` until Puppet reapplies;
  [SECRETS.md](SECRETS.md) states the removal step.

### NR-2026-028 — Per-session logout does not revoke the token *(Low, fixed in Track 8)*
* **Tier:** API, DAL · [`src/ServerServices/Security/TokenRevocationService.cs`](../../src/ServerServices/Security/TokenRevocationService.cs)
* **What was open:** Track 7 added *mass* revocation — a password change invalidates every
  outstanding token, and disabling a user takes effect on the next request — but there was no way to
  end one session. `SAMLLogout` returned the string `"Teste"`.
* **Fix:** a `revoked_tokens` table keyed on the `jti` claim (unique index `uq_revoked_tokens_jti`,
  db_version 82), consulted first in `JwtAuthenticationHandler.ValidateToken`, with rows pruned once
  past their `exp`. `POST /Sessions/Logout` revokes the token *the request was made with*, read from
  the Authorization header, and deliberately accepts no token from the caller — an endpoint that took
  a `jti` parameter would let any authenticated user sign out anybody else's session.
  `GET /Sessions/Current` reports whether the presented token is revoked, so a client can verify a
  sign-out took effect instead of assuming it did, which is precisely what this finding was.
* **Regression tests:**
  [`ServerServices.Tests/Track8/DeferredSecurityFixesInMemoryTest.cs`](../../src/ServerServices.Tests/Track8/DeferredSecurityFixesInMemoryTest.cs)
  — `TestARevokedTokenIsReportedAsRevoked`, `TestRevokingTwiceIsNotAnError`,
  `TestATokenWithNoJtiCannotBeRevokedIndividually`,
  `TestPruningRemovesOnlyRowsWhoseTokenHasExpired`; and
  [`API.Tests/APITests/SessionRevocationControllerTest.cs`](../../src/API.Tests/APITests/SessionRevocationControllerTest.cs)
  — 13 cases covering the header parsing (case-insensitive scheme, malformed token, no header at
  all), that the expiry is carried across so the pruning job can drop the row, and that a token with
  no `jti` is refused with a message naming the alternative rather than answering 200 to a sign-out
  that did not happen.

### NR-2026-032 — Biometric templates stored without column-level encryption *(Medium, fixed in Track 8)*
* **Tier:** ServerServices · [`src/ServerServices/Services/FaceIDService.cs`](../../src/ServerServices/Services/FaceIDService.cs)
* **Raised by:** the ASVS sweep, requirement 6.1.2
* **What was open:** the FaceID plugin stored its face representation and signature seed as plain
  strings. The reason this mattered is not exploitability — reading the column already requires
  database access, which is the whole dataset — it is irrevocability: a leaked password is rotated in
  a minute and a leaked face is not rotated at all.
* **Fix:** `FaceIdentification` and `SignatureSeed` go through `ISecretProtector` (AES-GCM) on write
  and are unprotected on read, exactly as the Track 4 integration credentials do.
  `ISecretProtector.LooksProtected` makes it an in-place upgrade with no migration: a row written
  before this change is read as-is and protected the next time it is written, so nobody has to
  re-enrol. AES-GCM over a few hundred bytes is microseconds, which is not measurable beside the
  vector comparison the matching path already does.
* **Regression test:** [`ServerServices.Tests/Track8/FaceIdTemplateProtectionInMemoryTest.cs`](../../src/ServerServices.Tests/Track8/FaceIdTemplateProtectionInMemoryTest.cs)
  — 7 cases. Two of them (`TestTheSignatureSeedIsProtectedOnWrite`,
  `TestTheProtectedSeedIsNotItsOwnPlaintext`) were **confirmed to fail on the pre-fix code** by
  reverting the two `Protect` calls and re-running. The other five are the ones that would break if
  the fix were done carelessly: a legacy plaintext row still works, a protected seed round-trips
  through transaction start, toggling an enrolled user off and on does not regenerate their seed
  (which would invalidate every anchor already issued), an unenrolled user is refused rather than
  crashing on `Convert.FromBase64String`, and the plugin gate still holds.
* **Not verified at runtime:** the FaceID plugin itself is not loaded in this environment, so the
  full enrol-then-match path over a real face was not exercised. What is exercised is the storage
  boundary — protect on write, reveal on read, legacy rows tolerated — which is what the finding was
  about.

---

## Accepted, with the mitigation Track 8 added

### NR-2026-027 — A plugin runs with the API's full authority *(Medium, risk-accepted; mitigation implemented)*
* **Tier:** Plugins · [`src/ServerServices/Security/PluginSignatureVerifier.cs`](../../src/ServerServices/Security/PluginSignatureVerifier.cs)
* **Status:** **Still accepted.** This is not closed and is not being presented as closed. A loaded
  plugin runs in the API process with the API's full authority; .NET removed Code Access Security and
  has no supported in-process sandbox, so there is no boundary to add. Recorded as **TM-A1** in the
  threat model.
* **What changed:** the mitigation the previous entry proposed is now implemented, and it changes the
  *trust decision* rather than the blast radius. `PluginsService` verifies a plugin assembly's
  publisher before loading it and logs who signed every load. With
  `plugins_require_signature` set, an unverified assembly is refused; with
  `plugins_trusted_publishers` set to a thumbprint list, only those publishers are accepted. "Any DLL
  in the plugins directory" becomes "a DLL from a publisher this installation named" — the difference
  between an attacker needing code execution as the service account and needing the publisher's
  signing key as well.
* **Two mechanisms, because one is not portable.** Authenticode
  (`X509Certificate.CreateFromSignedFile`) only reads embedded signatures on Windows, and the API is
  normally deployed on Linux. So the primary mechanism is a *detached* signature — `Foo.Plugin.dll.sig`
  beside `Foo.Plugin.dll.cer`, verified with the certificate's public key over the SHA-256 of the
  assembly. That works identically everywhere and needs no OS trust store, so an air-gapped
  installation can use it too.
* **Default is report-only.** An unsigned plugin loads with a warning naming the publisher gap unless
  the installation opts in to refusal. Defaulting to refusal would break every existing installation
  with a plugin on upgrade, which is how a security default gets switched off permanently.
* **Test:** [`ServerServices.Tests/Track8/PluginSignatureVerifierTest.cs`](../../src/ServerServices.Tests/Track8/PluginSignatureVerifierTest.cs)
  — 24 cases, most of them rejections, because the interesting case is adversarial: an attacker who
  can write to the plugins directory can also write a `.sig` and a `.cer` beside their DLL, so
  `TestAValidSignatureFromAnUnlistedPublisherIsNotTrusted` asserts that their own valid signature is
  refused when an allowlist is configured. Also covered: an assembly modified after signing, a
  signature from an expired or not-yet-valid certificate, garbage in the signature file, and
  thumbprints pasted colon-separated and lower-cased out of a certificate viewer.
* **What this does not do:** it does not stop a trusted publisher's plugin from reading the database
  or the signing key, and it does not verify the plugin's *behaviour*. The acceptance stands on the
  same reasoning as before — installing a plugin is trusting the operator who installed it — and the
  review date is unchanged at each minor release.

---

## Triage into milestones 7.2–7.5

Every finding above is assigned, per §7.1.3.

| Milestone | Findings |
|---|---|
| **7.2** Dependency & supply chain | NR-2026-031 (SBOM). Baseline scan: `dotnet list package --vulnerable --include-transitive` reported **no** vulnerable package across all 33 projects on 2026-08-26 — see [baseline-2026-08-26.md](baseline-2026-08-26.md). |
| **7.3** AuthN/AuthZ & secrets | NR-2026-001, 002, 003, 007, 008, 008b, 009, 010, 012, 018, 025, 028, 033 |
| **7.4** Data protection & transport | NR-2026-004, 005, 011, 013, 014, 015, 016, 019, 020, 026, 032 |
| **7.5** Continuous security | The gates that keep the above from recurring: `security.yml` (CodeQL, gitleaks, vulnerable-dependency, submodule provenance), [SECURITY.md](../../SECURITY.md), [TRIAGE_SLA.md](TRIAGE_SLA.md), [BURN_DOWN.md](BURN_DOWN.md) |
| **Risk-accepted** | NR-2026-024, 029, 030 (informational/low, reasons stated), NR-2026-027 (documented acceptance; the proposed provenance mitigation was implemented in Track 8 and the acceptance still stands) |
| **Raised by the ASVS sweep** | NR-2026-032, plus four requirements deliberately *not* raised as findings — password length/composition, breached-password lookup, anti-caching headers, XSD validation of scan files. The reasons are in [ASVS_L2_CHECKLIST.md](ASVS_L2_CHECKLIST.md), so silence there is not mistaken for oversight. |

---

## Regressions introduced by this track's own fixes

Six, all found and fixed before this landed. They are recorded rather than quietly corrected because
they are the strongest available argument for the discipline the rest of this register describes: a
security change that breaks the thing it protects is a security problem, and none of these six was
visible in a unit test of the component that caused it.

| # | The fix | What it broke | How it was caught |
|---|---|---|---|
| R1 | NR-2026-001, first attempt | Raised the *entropy* of a SAML request id the attacker did not have to guess, because they chose it. The one-click takeover was still open. | The repo's own `/security-review` gate, on its first run |
| R2 | NR-2026-015 | The API's `Content-Security-Policy` set `form-action 'none'`, and the NR-2026-001 rework then added a consent **form** to an API-served page. A browser refuses that submission, so **every desktop SSO sign-in would have failed** — while all 23 of the flow's own tests passed, because a controller test never sees a CSP. | A second adversarial review |
| R3 | NR-2026-015 | The middleware removed `Server` from the response collection and the header was still there: Kestrel writes it at the transport layer, below the pipeline. Needed `AddServerHeader = false`. | Curling a running instance, not the unit test over the policy object |
| R4 | NR-2026-014 | The API moved link hashing to SHA-256. The **WebSite** resolves those same rows — pushed to it verbatim over `/sync` — and still hashed with MD5, so every password-reset link would have failed to resolve, presenting as an expired link with nothing logged. | A second adversarial review |
| R5 | NR-2026-004/005 | The first-run server check read the certificate opt-in only from the persisted client store, while the error message it displayed told the operator to set `Server:AllowInvalidCertificate` in `appsettings.json`. Since that check gates whether the server URL is ever saved, a client facing a self-signed server **could never be configured at all**, and following the instruction verbatim changed nothing. | A second adversarial review |
| R6 | NR-2026-008 | The lockout counted an attempt against the account *and* the source address with the same four-failure budget. Behind a reverse proxy — the normal deployment — every client shares an address, so two colleagues mistyping their passwords would have locked out the whole organisation. | A second adversarial review |

Each now has its own regression test: `TheApiPolicyAllowsItsOwnConsentFormToBeSubmitted`,
`LinkKeyHashTest`, `TheApplicationSettingIsHonouredWhenNothingIsPersisted`,
`ASharedSourceAddressIsNotLockedOutByAFewColleaguesMistyping`, and — for R3, which no unit test can
see — the live header capture in [baseline-2026-08-26.md](baseline-2026-08-26.md) §4.

Two of the six (R2, R6) would have been *worse* than the vulnerability they were fixing: an SSO flow
nobody can complete, and a lockout that locks out the organisation rather than the attacker. The
pattern is consistent — a control tested only at the level it was written at looks correct, and the
break appears one layer up.

Two consistency items were fixed at the same time and are noted here rather than given their own
ids: `ReportsService` still derived report file names the way `FilesService` used to
(`SHA1(name + 15 characters)`), and now uses the same 256-bit capability token; and the `enc:v1:`
read path in `SecretProtector` keeps `Tools.Criptography.AES` alive, which is why that class now
carries a comment saying it is a read path and not a choice.

---

## Committed secret material — for the repository owner's decision

**Private keys are committed to this repository and its history.** They are:

```
src/API/Certificates/{certificate.pfx, certificate.pem, key.pem, localhost.pfx, localhost.key, demowebapp.local.pfx}
src/WebSite/Certificates/{the same set}
```

Assessed with `openssl`: all self-signed, all expired (`certificate.pfx` and `localhost.*` on
2023-09-14 and 2023-09-13; `demowebapp.local.pfx` valid to 2029 but for the fictional host
`demowebapp.local`), and the `.pfx` password is `"pass"` — published in `appsettings.json` in the same
commit. They are development fixtures, not production credentials, and no NetRisk installation should
ever have used them for a real host.

**Nothing was rewritten, force-pushed or deleted.** That decision is the repository owner's.

**Recommendation:**

1. Treat any certificate in that set as **compromised**. If any installation ever served with one,
   reissue it and rotate anything that travelled over a session it protected — in particular any
   password sent in a Basic-auth header.
2. Leave the history alone. These are expired self-signed fixtures; a history rewrite of a published
   repository costs every clone and fork a re-base for no security gain here.
3. Prevention, which is already in place: the Release-build refusal in NR-2026-003, the `.gitignore`
   patterns for key material, and the gitleaks configuration — whose allowlist names these files
   **individually** rather than excluding `*.pfx`, so the next key added to the same directory is
   still reported.

A repository-wide history scan for *other* secret types is part of the CI gate (`gitleaks` with
`fetch-depth: 0`), which runs on push and weekly. The manual sweep performed during this audit —
`git ls-files | xargs grep` over credential-shaped assignments in every tracked file — found no
API key, token, password or connection-string credential outside the certificate material above and
the test fixtures.
