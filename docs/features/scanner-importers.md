# Scanner importers and finding ingestion

> Track 3 (ASPM) milestones 3.1–3.4. Contract version: `1`.

NetRisk ingests scanner output through a single pipeline: an **importer** parses a report into
normalized findings, and the **ingestion service** decides what each finding means for the register.
The two are deliberately separate — an importer never touches the database, which is what makes a
third-party importer safe to load and trivial to unit-test.

```
report ──► importer (parse) ──► NormalizedFinding[] ──► ingestion (dedup, lifecycle, SLA) ──► register
```

## Built-in importers

| Name | Scanner | Formats | Full scan? | Notes |
|---|---|---|---|---|
| `nessus` | Tenable Nessus | `.nessus`, `.xml` | yes | Numeric severity 0–4 is authoritative; `risk_factor` is the fallback. |
| `sarif` | Any SARIF 2.1 producer | `.sarif`, `.json` | no | CodeQL, ESLint, Bandit, Checkov, gitleaks, … |
| `semgrep` | Semgrep | `.json`, `.sarif` | no | Native JSON preferred (carries a stable fingerprint); SARIF delegates to `sarif`. |
| `zap` | OWASP ZAP | `.json` | no | One finding per affected URL. |
| `trivy` | Aqua Trivy | `.json` | yes | Package CVEs, misconfigurations **and** secrets. |
| `openvas` | OpenVAS / Greenbone | `.xml` | yes | Parses the pipe-delimited NVT `<tags>` blob. |
| `burp` | Burp Suite | `.xml`, `.json` | no | Professional XML and Enterprise JSON. |
| `snyk` | Snyk Open Source / Container | `.json`, `.sarif` | yes | Snyk Code emits SARIF and delegates. |
| `grype` | Anchore Grype | `.json` | yes | Prefers CVSS v3 and resolves GHSA → CVE. |
| `dependabot` | GitHub Dependabot | `.json`, `.sarif` | yes | The `/dependabot/alerts` API body. |

The reserved name `auto` asks the server to identify the format from the file's content.

**"Full scan?"** is what licenses auto-close (see below). A `no` means the report covers only what
the tool was pointed at, so a finding's absence proves nothing.

## Severity mapping

Every importer maps its tool's vocabulary onto NetRisk's five-level scale
(`None`, `Low`, `Medium`, `High`, `Critical`) and **preserves the tool's own value** in
`raw_severity`, so a mapping decision stays auditable.

| Tool value | NetRisk | Importer |
|---|---|---|
| `0`–`4` | None … Critical | nessus |
| `error` / `warning` / `note` / `none` | High / Medium / Low / None | sarif |
| `ERROR` / `WARNING` / `INFO` | High / Medium / Low | semgrep |
| riskcode `3`/`2`/`1`/`0` | High / Medium / Low / None | zap |
| `CRITICAL` … `UNKNOWN` | Critical … None | trivy, snyk, grype, dependabot |
| threat `High` … `Log` | High … None | openvas |
| `High` … `Information` | High … None | burp |

Two mappings are deliberately not literal:

- **SARIF has no Critical.** Its ceiling is `error` → High. GitHub's
  `properties["security-severity"]` score is what makes Critical reachable, and it wins when higher.
- **Semgrep impact raises severity.** A rule at `WARNING` with `HIGH` impact is imported as High, so
  its findings stay comparable with the other scanners'.
- **Trivy secrets are at least High.** Trivy labels some secret rules Medium, which under-states a
  live credential.

A mapping can be overridden per import with an option: `severity.moderate=high`.

Any importer can also fall back to CVSS bands (≥9.0 Critical, ≥7.0 High, ≥4.0 Medium, >0 Low) when
the tool reports a score but no severity word.

## What is preserved

The normalized model carries more than the register's own columns, because deduplication and SLA
depend on it:

| Field | Why it matters |
|---|---|
| `ToolUniqueId` | The scanner's own stable id — the strongest dedup key there is. |
| `RuleId` | Stable across runs for a defect class, unlike the title, which vendors reword. |
| `Location` | `path:line`, a URL, or a package coordinate. The dedup identity for scanners with no asset. |
| `FirstSeen` | The SLA clock starts here. A report that carries a real date must not have it discarded. |
| `RawSeverity` | The tool's own severity string, so a mapping stays re-derivable. |
| `Evidence` | The matched line, the HTTP exchange, the plugin output — what a triager actually reads. |

Records an importer could not fully handle appear in `ImportResult.Warnings` and reach the import
summary. **Silent drops are the classic importer bug**: an import that lost a third of its rows
looks exactly like a clean one without them.

## Deduplication

Each importer has a **strategy chain**; the first strategy to produce a key wins, and that key is
persisted on the finding as `dedup_key`. It is never recomputed, so a heuristic change affects only
new imports.

| Strategy | Key |
|---|---|
| `UniqueIdFromTool` | The scanner's own id. Highest precedence when present. |
| `HashBased` | SHA-256 over an ordered field set. Default: `tool,ruleId,asset,location,cve`. |
| `LegacyHashCode` | The pre-Track-3 Nessus hash, so old rows still match. Compared against `import_hash`. |
| *plugin* | Any `IDeduplicationStrategyPlugin` an installed plugin contributes. |

Dedup **groups, it never discards**. A second sighting raises `detection_count`, moves
`last_detection`, and refreshes the scanner-derived fields — human-entered ones (comments,
assignment, technology) are never touched.

Configure per scanner under **Administration → Deduplication**, where the preview panel computes two
findings' keys and reports whether they would merge, without saving anything.

### Auto-close

Off by default, per scanner. When on, a finding the scanner previously reported but did not report
in a **full** scan is closed as Mitigated with a history event. A partial scan mistaken for a full
one closes everything outside its slice, which is far worse than a stale open finding — so a
partial report is refused with a warning rather than acted on.

## Finding lifecycle

`Active` → `Verified` → `Mitigated`, with `FalsePositive`, `OutOfScope`, `Duplicate` and
`RiskAccepted` as triage verdicts. Every state can be reopened to `Active`; nothing moves from a
suppressed state straight to `Mitigated`.

Two behaviours are load-bearing on re-import:

- **Sticky triage.** `FalsePositive`, `OutOfScope`, `RiskAccepted` and `Duplicate` survive the
  scanner reporting the finding again. Only `last_seen` moves.
- **Regression detection.** A `Mitigated` finding the scanner sees again is reopened as `Active`
  with a history event saying so.

Every transition writes an append-only `finding_status_history` row — who, when, why, and whether a
human, an import or a job did it. Suppressing transitions require a stated reason, enforced in the
service rather than only in the UI.

## SLA

`sla_due_date` is `first_detection` plus the remediation allowance for the finding's severity, under
the policy **in force when the finding appeared**. Policy rows are effective-dated and superseded
rather than edited, so a change never rewrites a past compliance number.

`DaysOverdue` is derived at read time, never stored, and returns null for suppressed states — a
finding nobody is allowed to work on does not accrue overdue days.

Defaults follow CISA: Critical 15 days, High 30, Medium 60, Low 90 (triage 2 / 5 / 10 / 15).

## Adding an importer

Implement `Contracts.Importers.IVulnerabilityReportImporter` (or
`INetriskVulnerabilityImporterPlugin` to ship it as a plugin) in the
[netrisk-plugin-sdk](https://github.com/ffquintella/netrisk-plugin-sdk). The rules:

1. **Do not touch the database, the network, or the file system.** Parse the stream, return records.
2. **Report what you could not parse.** Add an `ImportWarning` for every skipped or degraded record.
3. **Set `IsFullScan` honestly.** It is what licenses auto-close.
4. **Return `ImporterContract.Version` from `ContractVersion`.** A plugin claiming a newer contract
   than the host implements is refused with a clear message rather than failing mid-import.
5. **Make `CanHandle` specific.** Every JSON scanner's report starts the same way; sniff on
   something only your format has, or auto-detect will pick whoever is tried first.
