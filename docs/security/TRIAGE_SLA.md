# Internal Vulnerability Triage SLA

> Track 7 milestone 7.5.2 · First issued 2026-08-26
> Public-facing policy: [SECURITY.md](../../SECURITY.md) · Register: [FINDINGS.md](FINDINGS.md) · Progress: [BURN_DOWN.md](BURN_DOWN.md)

The internal companion to `SECURITY.md`. That document tells a reporter what to expect; this one
tells the project what it owes them, and — deliberately — the numbers are the **same ones NetRisk
ships as its product's default remediation SLAs** (Track 3.4). A security product that holds itself
to a looser standard than the one it enforces on its customers has an obvious problem.

---

## 1. Severity

Assigned with the [OWASP Risk Rating Methodology](https://owasp.org/www-community/OWASP_Risk_Rating_Methodology)
— likelihood × impact — not by CVSS alone. CVSS is recorded when a report supplies one, but the
decision is the risk rating, because CVSS routinely misprices findings in a self-hosted product where
"network accessible" and "authenticated" mean something different from a SaaS.

| Severity | Definition for NetRisk | Example from the 2026-08 audit |
|---|---|---|
| **Critical** | Unauthenticated compromise, authentication bypass, or disclosure of the finding register to a party who should not see it | NR-2026-001 (one-click SSO takeover), NR-2026-003 (published TLS private key in the default configuration) |
| **High** | Authenticated privilege escalation, cross-tenant access, credential disclosure, or loss of transport security | NR-2026-006 (arbitrary file write), NR-2026-007 (disabled user retains access) |
| **Medium** | Exploitable with a precondition (a permission, a network position, a user action) or a weakness that materially lowers the bar | NR-2026-013 (SSRF, needs `configuration`), NR-2026-015 (no security headers) |
| **Low** | Defence-in-depth, or a weakness with no current path | NR-2026-029 (`AllowedHosts: *`), NR-2026-028 (no per-session logout) |
| **Informational** | Worth recording, no action | NR-2026-024 (no CORS — the secure default) |

**Escalate one level** when any of these applies. They are the properties that make a finding worse
in *this* product specifically:

* the finding discloses vulnerability findings, hosts or scan files (asset A1 — the reason NetRisk is
  a target);
* the affected control was **documented as working**. This repository has shipped that twice, and it
  is worse than an absent control because it removes the reason to look;
* exploitation is silent — no log line, no audit row;
* the data at risk cannot be rotated (biometric templates, NR-2026-032).

---

## 2. Response times

Clock starts when a report reaches the private channel, or when a CI gate raises the finding.

| Severity | Acknowledge | Triage decision | Fix | Advisory |
|---|---|---|---|---|
| **Critical** | 24 h | 48 h | **7 days** | With the fix release |
| **High** | 48 h | 5 days | **30 days** | With the fix release |
| **Medium** | 72 h | 10 days | **90 days** | Changelog entry |
| **Low** | 72 h | 30 days | Next minor release | Changelog entry |
| **Informational** | 72 h | — | Recorded only | — |

**Acknowledge** means a human has read it and said so, with a tracking id. Not an auto-reply.
**Triage decision** means severity assigned, register entry created, milestone chosen, owner named.
**Fix** means a released version contains it — not merged, *released*. A fix on `main` that no
customer can install has not fixed anything.

NetRisk is maintained by a small team. If a target will be missed, the reporter is told **before** it
is missed, with a reason and a new date. Silence is the failure mode this table exists to prevent.

---

## 3. Triage steps

1. **Acknowledge**, with a tracking id.
2. **Reproduce.** A finding that cannot be reproduced is not triaged as low — it stays open with the
   reason. "We could not reproduce it" and "it does not happen" are different statements.
3. **Establish, do not assume.** Read the code path. If the answer is "that is handled by X", open X
   and confirm. Every entry in the register states *how* it was established for this reason.
4. **Rate** per §1, including the escalation criteria.
5. **Register** it in [FINDINGS.md](FINDINGS.md): id, severity with its likelihood/impact reasoning,
   tier and file, exploitability, proposed fix, owner, milestone.
6. **Fix with a test.** Every fix lands with a regression test that fails on the pre-fix code — the
   rule in [`src/AI_TESTING_INSTRUCTIONS.md`](../../src/AI_TESTING_INSTRUCTIONS.md), and the only
   thing that stops a finding recurring. Never weaken an assertion to get a green run.
7. **Verify.** Where the fix is observable at runtime, observe it. The 2026-08 baseline found a defect
   in its own header fix only by running a scan against a live instance — Kestrel re-adds the `Server`
   header below the middleware, which no unit test over the policy object could see.
8. **Release and disclose** per §2.
9. **Update** [BURN_DOWN.md](BURN_DOWN.md).

---

## 4. Who

| Role | Responsibility | Currently |
|---|---|---|
| Security contact | First response, severity, coordination with the reporter | Repository maintainer |
| Fix owner | Implementation, tests, release | Assigned per finding, named in the register |
| Release manager | Advisory text, version, publication | Repository maintainer |

A single maintainer holds all three today. That is stated rather than papered over: it is the
project's largest process risk, and it is why the automated gates matter more here than they would in
a team with a rota — a gate does not go on holiday.

---

## 5. Risk acceptance

Not every finding gets fixed, and that is legitimate — but only in the same shape the product itself
demands of its users (Track 3.2.3):

* a **stated reason** that survives being read aloud. "Not exploitable" without saying why is not a
  reason;
* a **named owner**;
* an **expiry date**, after which it is re-decided rather than inherited;
* the entry stays in the register as *Accepted*, not deleted. A deleted finding is a finding nobody
  will re-examine.

The same rule is machine-enforced for dependencies: an entry in
[`security/dependency-suppressions.yml`](../../security/dependency-suppressions.yml) needs an
advisory id, an owner, a reason and an expiry at most 180 days out, and an **expired suppression
fails the build**. That is the file's whole design — a permanent suppression is a decision nobody
revisits, and a file full of them is indistinguishable from having no gate.

---

## 6. Gate discipline

Every CI gate in [`.github/workflows/security.yml`](../../.github/workflows/security.yml) fails on
something **new**, never on the backlog. This is not leniency; it is the only way a gate survives. A
gate that fires on forty pre-existing findings is disabled within a week, and a disabled gate is
worse than no gate because it looks like coverage.

So: baseline what exists, fail on the delta, and burn the baseline down deliberately through
[BURN_DOWN.md](BURN_DOWN.md) rather than by turning the gate off.

A suppression — in CodeQL's config, in the dependency file, anywhere — needs a stated reason and a
cross-reference into the register. An entry without one is not a suppression, it is a shrug.
