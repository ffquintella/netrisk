# Security Policy

NetRisk is a risk, vulnerability and incident management product. It stores the record of what is
*not yet fixed* on its users' estates, which makes an installation a high-value target in its own
right. We try to hold the product to the standard it helps its users enforce.

Internal process behind this policy: [docs/security/TRIAGE_SLA.md](docs/security/TRIAGE_SLA.md).
Everything else security-related: [docs/security/](docs/security/).

---

## Reporting a vulnerability

**Please do not open a public issue for a security problem.**

Use either:

1. **GitHub private vulnerability reporting** — the *Security* tab → *Report a vulnerability*.
   Preferred: it keeps the report, the discussion and the eventual advisory in one place.
2. **security@netrisk.app**, if you would rather not use GitHub. PGP on request.

### What helps

* the affected version or commit, and which component (API, GUIClient, WebSite, BackgroundJobs,
  ConsoleClient, a plugin);
* what an attacker gains, and what they need to start — unauthenticated? a particular permission? a
  network position?
* the smallest reproduction you have. A request, a file, a few lines of code;
* your assessment of severity, if you have one. We will rate it ourselves, but yours is a useful
  cross-check;
* how you would like to be credited, or that you would rather not be.

An imperfect report is worth far more than a delayed one. Send what you have.

---

## What we will do

| | Critical | High | Medium | Low |
|---|---|---|---|---|
| Acknowledge | 24 h | 48 h | 72 h | 72 h |
| Triage decision | 48 h | 5 days | 10 days | 30 days |
| Fix released | **7 days** | **30 days** | **90 days** | Next minor release |

*Acknowledge* means a person has read it and replied with a tracking id — not an automated receipt.
*Fix released* means a version you can install contains it, not a merge to `main`.

These are the same numbers NetRisk ships as its product's default remediation SLAs. A security
product that gives itself more time than it gives its users has an obvious problem.

NetRisk is maintained by a small team. **If a target is going to be missed, we will tell you before
it is missed**, with the reason and a new date. Silence is the thing we are trying not to do.

### Coordinated disclosure

* Default embargo: **90 days** from acknowledgement, or the day a fix is released, whichever comes
  first.
* We will agree a date with you rather than impose one. If a fix needs longer, we will say why and
  ask.
* If a vulnerability is being exploited, we will move immediately and coordinate the announcement
  with you rather than wait out the window.
* You will be credited in the advisory and the changelog unless you ask not to be.
* A CVE will be requested for anything Medium or above that affects a released version.

---

## Safe harbour

We will not pursue or support legal action against anyone who, in good faith:

* finds and reports a vulnerability through the channels above;
* stays within **their own** installation, or a test installation they control;
* does not access, modify or retain data belonging to anyone else;
* does not degrade service for other users;
* gives us a reasonable chance to fix it before going public.

If you are unsure whether something is in scope, ask first — security@netrisk.app. We would rather
answer a question than receive a report of something you were not sure you were allowed to try.

### Out of scope

Not because they do not matter, but because a report of one tells us nothing we can act on:

* findings from an automated scanner with no demonstrated impact;
* denial of service by volume against a self-hosted instance the reporter controls;
* missing hardening headers on an endpoint with no sensitive response, absent an actual attack;
* social engineering of maintainers or users;
* vulnerabilities in a third-party dependency — report those upstream, then tell us so we can pin or
  patch. See [docs/security/SUPPLY_CHAIN.md](docs/security/SUPPLY_CHAIN.md);
* anything requiring physical access to the server, or an already-compromised administrator account;
* the **plugin system**. A NetRisk plugin is a .NET assembly loaded in-process, so it has the
  application's full authority by construction. This is a documented, accepted design limitation —
  see finding NR-2026-027 — and .NET offers no in-process sandbox to fix it with. A report that a
  plugin can do something a plugin can do is not a finding; a report that a plugin can be *loaded*
  without the operator installing it very much is.

---

## Supported versions

Security fixes are released for:

| Version | Supported |
|---|---|
| 2.16.x | ✅ |
| 2.15.x | ✅ until 2027-02 |
| < 2.15 | ❌ |
| 1.x | ❌ |

The two most recent minor versions are supported. When a new minor ships, the oldest of the three
enters a six-month grace period and is then dropped.

**Versions before 2.16.3 have known critical issues** — the Track 7 audit found three, including a
one-click account-takeover in the desktop single-sign-on flow and a shipped configuration that served
TLS with a private key published in this repository. See
[docs/security/FINDINGS.md](docs/security/FINDINGS.md). Upgrade.

---

## What we do on our side

* **Every release** runs CodeQL, gitleaks over the full history, a known-vulnerable-dependency gate
  and a submodule-provenance check — [`.github/workflows/security.yml`](.github/workflows/security.yml).
* **Every artifact** ships with a CycloneDX SBOM and a SHA-256 checksum, and is code-signed
  (Authenticode / Developer ID with notarisation).
* **Every minor release** re-runs the audit: the `/security-review` gate, the ASVS checklist for
  changed areas, a threat-model delta, and an updated
  [burn-down](docs/security/BURN_DOWN.md). A release does not ship with an untriaged critical
  finding.
* **Every security fix lands with a regression test** that fails on the code before it. We do not
  weaken an assertion to get a green run.
