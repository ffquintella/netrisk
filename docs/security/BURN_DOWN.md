# Remediation Burn-Down

> Track 7 milestone 7.5.3 · Updated at each minor release
> Register: [FINDINGS.md](FINDINGS.md) · SLAs: [TRIAGE_SLA.md](TRIAGE_SLA.md)

Open findings by severity over time. Updated when a finding is opened, fixed or accepted — not
reconstructed at release time from memory.

---

## Open findings over time

| Date | Release | Critical | High | Medium | Low | Info | Open total | Accepted | Note |
|---|---|---|---|---|---|---|---|---|---|
| 2026-08-26 | pre-2.16.3 | 3 | 7 | 19 | 3 | 2 | **34** | 0 | Track 7 audit opens the register: every finding raised at once |
| 2026-08-26 | pre-2.16.3 | 0 | 0 | 4 | 1 | 0 | **5** | 4 | Same day, after remediation: 25 fixed, 4 accepted |
| 2026-08-26 | pre-2.17.0 | 0 | 0 | 0 | 0 | 0 | **0** | 4 | Track 8 closes the five deferred findings (008b, 017, 025, 028, 032); 027 stays accepted with its mitigation implemented |

```
Open findings, by severity

  Track 7 in    ████████████████████████████████ 34  C:3  H:7  M:19  L:3  I:2
  Track 7 out   █████ 5                              C:0  H:0  M:4   L:1  I:0
  Track 8 out    0                                   C:0  H:0  M:0   L:0  I:0
                ▲
                └─ 30 fixed with regression tests, 4 risk-accepted with stated reasons
```

The first three rows share a date because Track 7 was a single audit-and-remediate pass and Track 8
followed it in the same working day: the register was created, mostly closed, and then fully closed.
Later rows will be one per release.

**Zero open is not zero risk.** It means every finding this audit raised has either a fix with a test
behind it or a written acceptance — not that nothing is left to find. The four acceptances below are
live exposures somebody decided to live with, and the largest of them (NR-2026-027, a plugin running
with the API's full authority) cannot be fixed at all within .NET's current capabilities.

---

## Closed in Track 8, and what proves each

The five rows that were open. Every one names a test rather than a commit, because a commit says
something changed and a test says what it now does.

| Id | Severity | What was open | What closed it | Proof |
|---|---|---|---|---|
| [NR-2026-008b](FINDINGS.md#nr-2026-008b--brute-force-counters-are-per-process-and-in-memory-medium-fixed-in-track-8) | Medium | Lockout counters per process, so a multi-instance deployment gave the budget per instance | `PersistedLoginAttemptTracker` over a `login_attempts` table, unique on `(identity, source)` | `DeferredSecurityFixesInMemoryTest` (5 cases) + `Track8GovernanceSchemaTests.Phase13_TheRevocationListAndLockoutCounterAreUniquelyKeyed` against real MariaDB |
| [NR-2026-017](FINDINGS.md#nr-2026-017--no-per-file-access-control-on-attachments-medium-fixed-in-track-8) | Medium | No per-file ACL; the unique name was the whole capability | `nr_files.entity_id` under the Track 2.3 query filter, plus `IFileAccessAuthorizer` resolving the parent's rules | `DeferredSecurityFixesInMemoryTest` (7 cases) + `API.Tests/FileAccessControlTest` (6) + the backfill asserted on a real database |
| [NR-2026-025](FINDINGS.md#nr-2026-025--deployment-templates-write-the-database-password-to-disk-medium-fixed-in-track-8) | Medium | Puppet rendered the database password into `appsettings.json` on every host | `Database__ConnectionString` in a `0600` `netrisk.env`, `show_diff => false`; the `db_*` parameters removed from the appsettings templates entirely | `Packaging.Tests/DeploymentSecretPlacementTest` (14 cases); fails on the pre-fix templates. The 2.17.0 follow-up regression — the entrypoints read the file with `.`, and the shell ate the connection string at its first `;` — is pinned by `Packaging.Tests/DeploymentEnvironmentFileLoaderTest` |
| [NR-2026-028](FINDINGS.md#nr-2026-028--per-session-logout-does-not-revoke-the-token-low-fixed-in-track-8) | Low | "Sign out this one session" needed per-`jti` state; `SAMLLogout` returned `"Teste"` | `revoked_tokens` keyed on `jti`, consulted in `JwtAuthenticationHandler`; `POST /Sessions/Logout` revokes the presented token only | `DeferredSecurityFixesInMemoryTest` (4) + `API.Tests/SessionRevocationControllerTest` (13) |
| [NR-2026-032](FINDINGS.md#nr-2026-032--biometric-templates-stored-without-column-level-encryption-medium-fixed-in-track-8) | Medium | FaceID templates not column-encrypted; a face cannot be rotated | `ISecretProtector` on write, reveal on read, `LooksProtected` making it an in-place upgrade | `FaceIdTemplateProtectionInMemoryTest` (7); two of them confirmed failing on the pre-fix code by reverting the `Protect` calls |

Two of these carry a stated residual rather than a clean close, and both are in
[FINDINGS.md](FINDINGS.md): the persisted lockout counter is a database write on a refused login (the
per-source rate limiter is what keeps that from being a lever), and an attachment whose parent carries
no entity keeps a NULL `entity_id` and stays visible, which is the honest outcome rather than a guess
at which tenant it belongs to.

---

## Accepted, with a review date

| Id | Severity | Decision | Review |
|---|---|---|---|
| [NR-2026-024](FINDINGS.md#nr-2026-024--no-cors-middleware-on-the-api-informational-no-action) | Info | No CORS policy is the secure default; recorded so a future addition is recognised as a security decision | Whenever CORS is proposed |
| [NR-2026-027](FINDINGS.md#nr-2026-027--a-plugin-runs-with-the-apis-full-authority-medium-risk-accepted-mitigation-implemented) | Medium | .NET has no in-process sandbox; installing a plugin is trusting the operator who installed it. **Still accepted.** The proposed mitigation shipped in Track 8: `PluginSignatureVerifier` checks the publisher before load and logs who signed it, report-only by default. That changes the trust decision, not the blast radius | Each minor release |
| [NR-2026-029](FINDINGS.md#nr-2026-029--allowedhosts--low-risk-accepted) | Low | `AllowedHosts: *`; reset links come from configuration rather than the `Host` header, so the usual exploit does not apply. Deployment checklist recommends an explicit list | 2027-02 |
| [NR-2026-030](FINDINGS.md#nr-2026-030--enablesensitivedatalogging-present-informational-no-action) | Info | Sensitive-data logging is inside `#if DEBUG` *and* behind a config flag; unreachable in a Release binary | — |

Plus the threat model's own acceptances, TM-A1 to TM-A5, in [THREAT_MODEL.md](THREAT_MODEL.md) §5.

---

## What closed, and what proves it

Thirty findings, each with a regression test that fails on the pre-fix code — except NR-2026-033, whose proof is empirical (the WebSite was started with the value in the environment and honoured it). Grouped by what
they were:

| Class | Findings | Proof |
|---|---|---|
| Authentication and session | 001, 007, 008, 010, 012, 018 | `SamlSignInFlowTest`, `LoginAttemptTrackerTest`, the `API.Tests` authentication suite |
| Authorization | 009 | `ControllerAuthorizationInventoryTest` — and it now fails if a *new* unannotated endpoint appears |
| Cryptography and randomness | 002, 003, 011, 014 | `RandomGeneratorTest`, `AesGcm256Test`, `SecretProtectorTest`, `CommittedCertificatesTest` |
| Transport | 004, 005, 015, 016, 026 | `ServerCertificatePolicyTest`, `SecurityHeaderPolicyTest`, plus a **live** header and TLS scan in [baseline-2026-08-26.md](baseline-2026-08-26.md) |
| Injection and traversal | 006, 021, 022, 023 | `SafePathToolTest`, `FilesServiceUploadPathTest`, `ImporterXxeTest`, `ExternalUrlPolicyTest` |
| SSRF | 013 | `OutboundUrlPolicyTest` |
| Timing | 019 | Constant-time comparison in `IssueTrackerService` |
| Storage hygiene | 020 | Staging directory moved off `/tmp`, `0700` |
| Configuration and supply chain | 031, 033 | `SbomTest`, `ContinuousSecurityConfigurationTest`, and the environment-provider fix verified by running the WebSite with `LocalDb__ConnectionString` set |
| Closed in Track 8 | 008b, 017, 025, 028, 032 | `DeferredSecurityFixesInMemoryTest`, `FaceIdTemplateProtectionInMemoryTest`, `FileAccessControlTest`, `SessionRevocationControllerTest`, `DeploymentSecretPlacementTest`, `Track8GovernanceSchemaTests` |

---

## Cadence

Per [TRIAGE_SLA.md](TRIAGE_SLA.md) §2 and milestone 7.5.3, at each **minor** release:

1. run `/security-review` over the release branch, commit `baseline-<date>.md`;
2. run `./scripts/security/scan-dependencies.sh`, refresh the SBOM;
3. re-walk the changed areas of [ASVS_L2_CHECKLIST.md](ASVS_L2_CHECKLIST.md);
4. refresh the [threat model](THREAT_MODEL.md) deltas and re-date its accepted risks;
5. add a row above and reconcile the open and accepted tables.

**A release cannot ship with an untriaged critical finding.** Untriaged, not unfixed — a critical
finding may ship if it has been rated, registered, and consciously accepted or scheduled. What may
not ship is one nobody has looked at.

### Tracking this inside NetRisk itself

The spec suggests, fittingly, tracking these as risks inside NetRisk. Recommended mapping for an
installation that wants to:

* one **risk** per open finding, with the register id in the title;
* the *severity* here as the risk's impact, and the *exploitability* note as its likelihood;
* the Track 3.2.3 **risk acceptance** feature, with its expiry, for the Accepted table above — which
  is the same discipline this document asks of the dependency suppression file;
* the Track 3.4 **SLA configuration** set to the numbers in [TRIAGE_SLA.md](TRIAGE_SLA.md) §2, which
  are already NetRisk's shipped defaults.
