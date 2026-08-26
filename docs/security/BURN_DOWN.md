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

```
Open findings, by severity, 2026-08-26

  before   ████████████████████████████████ 34  C:3  H:7  M:19  L:3  I:2
  after    █████ 5                              C:0  H:0  M:4   L:1  I:0
           ▲
           └─ 25 fixed with regression tests, 4 risk-accepted with stated reasons
```

The two rows share a date because Track 7 was a single audit-and-remediate pass: the register was
created and mostly closed in the same change. Later rows will be one per release.

---

## Open, with the reason each is open

| Id | Severity | What is open | Blocked on | Owner | Target |
|---|---|---|---|---|---|
| [NR-2026-008b](FINDINGS.md#nr-2026-008b--brute-force-counters-are-per-process-and-in-memory-medium-open) | Medium | Lockout counters are per process, so a multi-instance deployment gets the budget per instance | A persisted counter (schema change) or a shared cache | security@netrisk.app | Track 8 |
| [NR-2026-017](FINDINGS.md#nr-2026-017--no-per-file-access-control-on-attachments-medium-open-mitigated) | Medium | No per-file ACL on attachments; the unique name is the capability | An authorization model for files reachable through six different parents | security@netrisk.app | Track 8 |
| [NR-2026-025](FINDINGS.md#nr-2026-025--deployment-templates-write-the-database-password-to-disk-medium-open) | Medium | The Puppet module still renders the database password into `appsettings.json` | Moving it to an `EnvironmentFile` — now possible, since NR-2026-033 added the environment provider | security@netrisk.app | Track 8 |
| [NR-2026-032](FINDINGS.md#nr-2026-032--biometric-templates-stored-without-column-level-encryption-medium-open) | Medium | FaceID templates not column-encrypted; a face cannot be rotated | Touching the plugin's matching hot path, which cannot be exercised here | security@netrisk.app | Track 8 |
| [NR-2026-028](FINDINGS.md#nr-2026-028--per-session-logout-does-not-revoke-the-token-low-open) | Low | "Sign out this one session" needs per-`jti` state | A small `revoked_tokens` table | security@netrisk.app | Track 8 |

Three of the five (008b, 017, 028) are **residuals** of a fix that landed: the exposure is materially
reduced rather than untouched. All five are Medium or Low. **No critical or high finding is
outstanding.**

---

## Accepted, with a review date

| Id | Severity | Decision | Review |
|---|---|---|---|
| [NR-2026-024](FINDINGS.md#nr-2026-024--no-cors-middleware-on-the-api-informational-no-action) | Info | No CORS policy is the secure default; recorded so a future addition is recognised as a security decision | Whenever CORS is proposed |
| [NR-2026-027](FINDINGS.md#nr-2026-027--a-plugin-runs-with-the-apis-full-authority-medium-risk-accepted-open) | Medium | .NET has no in-process sandbox; installing a plugin is trusting the operator who installed it. Mitigation proposed: signature verification before load | Each minor release |
| [NR-2026-029](FINDINGS.md#nr-2026-029--allowedhosts--low-risk-accepted) | Low | `AllowedHosts: *`; reset links come from configuration rather than the `Host` header, so the usual exploit does not apply. Deployment checklist recommends an explicit list | 2027-02 |
| [NR-2026-030](FINDINGS.md#nr-2026-030--enablesensitivedatalogging-present-informational-no-action) | Info | Sensitive-data logging is inside `#if DEBUG` *and* behind a config flag; unreachable in a Release binary | — |

Plus the threat model's own acceptances, TM-A1 to TM-A5, in [THREAT_MODEL.md](THREAT_MODEL.md) §5.

---

## What closed, and what proves it

Twenty-five findings, each with a regression test that fails on the pre-fix code — except NR-2026-033, whose proof is empirical (the WebSite was started with the value in the environment and honoured it). Grouped by what
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
