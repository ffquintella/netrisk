# Security Documentation

Track 7 deliverables. Start here.

| Document | What it is | When to read it |
|---|---|---|
| [THREAT_MODEL.md](THREAT_MODEL.md) | Assets, six trust boundaries, STRIDE per boundary, control mapping, accepted risks | Before designing anything that crosses a tier |
| [FINDINGS.md](FINDINGS.md) | The findings register — 34 findings, how each was established, the fix and its test | To see what was wrong and what is still open |
| [ASVS_L2_CHECKLIST.md](ASVS_L2_CHECKLIST.md) | OWASP ASVS Level 2, requirement by requirement, with evidence | To check whether a specific control exists |
| [baseline-2026-08-26.md](baseline-2026-08-26.md) | What was actually run, and what it produced | To tell measurement from assertion |
| [SUPPLY_CHAIN.md](SUPPLY_CHAIN.md) | Dependency scanning, SBOM, submodule provenance and review procedure | Before bumping a dependency or a submodule |
| [SECRETS.md](SECRETS.md) | Every secret, where it lives, how to rotate it, and a deployment checklist | Before deploying, and when something leaks |
| [DATA_PROTECTION.md](DATA_PROTECTION.md) | Classification, encryption at rest and in transit, headers, cookies | To understand what is protected and what is not |
| [TRIAGE_SLA.md](TRIAGE_SLA.md) | Internal severity definitions and response times | When a report arrives or a gate fires |
| [BURN_DOWN.md](BURN_DOWN.md) | Open findings over time; what closed and what proves it | At each release |

Reporting a vulnerability: [SECURITY.md](../../SECURITY.md) at the repository root.

---

## The rule these documents follow

Every security claim names the code or the test that establishes it. Never "handled", never "by
design", never a comment.

That is not pedantry. NetRisk has twice shipped a control that was documented as working and was not:
multi-entity scoping (`ApplyEntityScope` was called from one query with a null principal, so it
filtered nothing — every authenticated user could read every tenant's data), and a "complete" Master
Dashboard backend that did not exist. The 2026-08 audit found the same pattern a third time:
`WebAuthnController`'s own doc comment said "The registration endpoints are authenticated" while the
class carried no `[Authorize]` attribute at all.

So a claim without a name is treated as unverified, and a review that cannot name the test must
downgrade the claim.

**Six of this track's own fixes were themselves wrong**, and every one was caught by looking rather
than by a passing test — which is the argument for the rule in miniature. Two would have been worse
than the vulnerability they fixed: a Content-Security-Policy that forbade the very consent form the
SSO fix depends on (so no desktop sign-in could complete), and a lockout keyed on a source address
that behind a reverse proxy is shared by the whole organisation. The full list, with what caught each
one, is in [FINDINGS.md](FINDINGS.md) § "Regressions introduced by this track's own fixes".

## Automation

| Gate | Where | Runs |
|---|---|---|
| CodeQL (C#) | [`.github/workflows/security.yml`](../../.github/workflows/security.yml) | Push, pull request, weekly |
| gitleaks, full history | same | same |
| `dotnet list package --vulnerable` | same, via [`scripts/security/scan-dependencies.sh`](../../scripts/security/scan-dependencies.sh) | same |
| Submodule provenance review | same, via [`scripts/security/check-submodule-bump.sh`](../../scripts/security/check-submodule-bump.sh) | Pull request |
| Dependabot (NuGet ×2, Actions, submodules) | [`.github/dependabot.yml`](../../.github/dependabot.yml) | Weekly |
| CycloneDX SBOM | [`build/Build.Sbom.cs`](../../build/Build.Sbom.cs) | Every `Package*` target |

Each gate fails on something **new**, never on the backlog — see [TRIAGE_SLA.md](TRIAGE_SLA.md) §6
for why that is the only way a gate survives.

Both scripts run locally, on purpose:

```bash
./scripts/security/scan-dependencies.sh
BASE_REF=<sha> HEAD_REF=<sha> PR_BODY="$(cat msg.txt)" ./scripts/security/check-submodule-bump.sh
```
