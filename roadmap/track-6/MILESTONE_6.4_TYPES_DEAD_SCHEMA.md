# Milestone 6.4 — Type Standardization & Dead Schema Removal

**Track:** 6 — Database Uniformization & Schema Health
**Plan mapping:** [docs/plano-uniformizacao-banco.md](../../docs/plano-uniformizacao-banco.md) — *Fase 5* + *Fase 6*
**Status:** Not started
**Risk:** Medium (Phase 5 — data migration via create-copy-coexist) to controlled (Phase 6 — never a drop without an archived dump + observation cycle)

## Goal

Standardize temporal and status types, then stage the removal of unused schema objects. Every data migration follows **create new → copy → coexist for one release → remove later**, and every removal follows **deprecate → observe one release → archive → drop**. At no point does a drop happen without a backing dump and an observation window — both enforced by the `--phase 6b` gate in the 6.1 tool.

## Part A — Phase 5: Temporal & status type standardization

1. **`created_at` / `updated_at` → DATETIME UTC.** Remove implicit `ON UPDATE CURRENT_TIMESTAMP` where the application already controls the value (auto-update TIMESTAMP produces phantom updates).
2. **`risks.status` (varchar(20)) → int + C# enum** — the principal case. Use create-copy-coexist:
   - add a new `status_id` column,
   - populate it by mapping the distinct existing string values,
   - run a full release with both columns coexisting,
   - drop the old `status` column **only in Phase 6** — never in the same migration that creates the new one.
3. **Explicit enum conversion:** `BiometricTransaction.TransactionResult` → explicit `HasConversion` in EF.

**Why medium risk:** requires data migration. The create-copy-coexist pattern guarantees rollback at any point because the old column is never dropped in the same release.

## Part B — Phase 6: Deprecation & removal (two passes)

> **Never drop directly.** Pattern: **deprecate → observe one release → archive → drop.** The destructive pass is gated by `database upgrade-schema --phase 6b`, which refuses to run until 6a has been logged in `schema_upgrade_log` for ≥ the observation window, requires explicit `--yes`, and dumps each table before dropping.

### Pass 6a — Deprecate (reversible)

1. Remove the `DbSet`s and mappings of the dead entities from `NRDbContext` (the app stops seeing them; tables and data stay intact).
2. Rename the tables in the DB with a `zz_deprecated_` prefix (pure rename, data preserved) — any forgotten access fails loud and fast.
3. Mark the C# entities `[Obsolete]` before deleting them from the project.

### Pass 6b — Remove (after ≥1 release with no incident)

4. Export each deprecated table with its data to an archived dump (`mysqldump --tables`).
5. Final migration with `DropTable` + deletion of the entity files.

### Confirmed candidates (zero references outside DAL/Migrations)

**Dead functional tables:** `ContributingRisksImpact`, `ContributingRisksLikelihood` (and evaluate parent `contributing_risks`), `QuestionnairePendingRisk`, `ResidualRiskScoringHistory`, `FrameworkControlTestResultsToRisk`, `FrameworkControlTypeMapping`, `PermissionToPermissionGroup`, `MitigationAcceptUser`, `RiskToAdditionalStakeholder`, `RiskToLocation`, `RiskToTechnology`, `FrameworkControlTestComment`, `FrameworkControlTestAudit`, `FailedLoginAttempt`, `UserPassHistory`.

**Dead enumeration tables:** `ControlPhase`, `ControlType`, `FileTypeExtension`, `Regulation`, `RiskFunction`, `TestStatus`, `ThreatCatalog`, `ThreatGrouping`. (`RiskGrouping` has 1 use in `StatisticsService` — investigate before touching.)

**Orphan columns:** `risks.regulation`, `risks.project_id` (if 6.3 confirmed it dead), `risks.status` (old, after Phase 5). Same pattern: stop mapping → observation release → drop.

### Special attention (do not remove blindly)

- **`FailedLoginAttempt`** and **`UserPassHistory`** are security tables — confirm that login lockout and password-reuse prevention are not *pending requirements* before removing. These may be features to **implement**, not tables to delete.
- **`UserPassReuseHistory`** exists separately — determine which of the two (`UserPassHistory` vs. `UserPassReuseHistory`) is the live one before deprecating either.
- Cross-check every candidate's row count against the **Phase 0 census** from 6.1: a candidate *with* data is archived (and double-checked for hidden use), an empty one can move faster.

## Acceptance criteria

### Phase 5
- [ ] `created_at`/`updated_at` are DATETIME UTC across the standardized tables; no unintended `ON UPDATE CURRENT_TIMESTAMP` remains where the app sets the value.
- [ ] `risks.status_id` (int) added and populated from the distinct existing string values, with both columns coexisting after this release (old column **not** dropped here).
- [ ] A documented value→enum mapping exists and round-trips; the C# `Risk` enum + `HasConversion` is wired.
- [ ] `BiometricTransaction.TransactionResult` has an explicit `HasConversion`.
- [ ] `Down()` restores the old column/state.

### Phase 6
- [ ] Every candidate re-confirmed as zero-reference against the current codebase at execution time (not just the plan snapshot).
- [ ] `RiskGrouping`, `FailedLoginAttempt`, `UserPassHistory`/`UserPassReuseHistory` explicitly investigated and a keep/remove decision recorded with rationale.
- [ ] Pass 6a: dead entities unmapped, tables renamed `zz_deprecated_*`, C# entities marked `[Obsolete]`; nothing dropped.
- [ ] Observation release elapsed and logged in `schema_upgrade_log` before 6b.
- [ ] Pass 6b: per-table `mysqldump` archive produced and verified for every dropped table **before** the `DropTable`; `--phase 6b` gate enforced `--yes` + the time window.
- [ ] `risks.status` old column, `risks.regulation`, and `risks.project_id` (if dead) dropped only after their coexistence/observation release.
- [ ] `dotnet test src/netrisk.sln` green after both passes; GUIClient smoke passes for risks, biometrics/FaceID, framework controls, and any feature that referenced a removed enum table.
- [ ] CHANGELOG updated for each pass; applied to homolog via the 6.1 tool with `--dry-run` attached before prod.

## Verification

- `--phase 5 --dry-run` impact report (status value distribution, row counts).
- Status mapping round-trip test on a clone before coexistence release.
- 6a: confirm app runs with entities unmapped and `zz_deprecated_*` tables present but unused.
- 6b gate: confirm refusal when the observation window hasn't elapsed; confirm dump-before-drop ordering.
- Full test suite green after each pass.

## Dependencies & ordering

- **Depends on:** 6.3 (for `Risk.ProjectId`/`risks.status` removal decisions) and 6.1 (the `6b` gate + Phase 0 census).
- 6a and 6b are **separate releases** with at least one release of observation between them.
- Phase 5 and Phase 6 are their own releases.

```
6.1 ─► 6.2 ─► 6.3 ─► 6.4 Phase 5 ─► 6.4 Phase 6a ─► (≥1 release observation) ─► 6.4 Phase 6b
```

## Out of scope

- New security features (login lockout, password-reuse enforcement) — flagged here as a *decision input*, but implementing them is separate work (relevant to Track 7).
