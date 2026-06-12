# Milestone 6.4 — Type Standardization & Dead Schema Removal

**Track:** 6 — Database Uniformization & Schema Health
**Plan mapping:** [docs/plano-uniformizacao-banco.md](../../docs/plano-uniformizacao-banco.md) — *Fase 5* + *Fase 6*
**Status:** Completed (`db_version` 71 → 73)
**Risk:** Medium (Phase 5 — data migration via create-copy-coexist) to controlled (Phase 6 — never a drop without an archived dump + observation cycle)

> **Decision (temporal types):** the `ON UPDATE CURRENT_TIMESTAMP` columns were **kept** rather than stripped — they are audit timestamps that should keep auto-updating. So Phase 5's temporal item reduced to confirming those columns stay mapped/used; the substantive Phase 5 work is the `risks.status` → `status_id` migration and the explicit enum conversions.

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
- [x] Temporal: `ON UPDATE CURRENT_TIMESTAMP` columns deliberately **kept** as auto-updating audit timestamps (see Decision above) — they remain mapped/used; no temporal-type churn.
- [x] `risks.status_id` (int) added and populated from the distinct existing string values, with both columns coexisting after this release (old column **not** dropped here).
- [x] A documented value→enum mapping exists and round-trips (`RiskHelper.GetRiskStatusName`/`GetRiskStatusFromName`); the C# `DAL.Enums.RiskStatus` enum + explicit `HasConversion<int>()` is wired.
- [x] `BiometricTransaction.TransactionResult` has an explicit `HasConversion<int>()`.
- [x] `Down()` restores the old column/state (`Track6Phase5StatusTypeStandardization.Down()` drops `status_id`).

### Phase 6
- [x] Every candidate re-confirmed as zero-reference against the current codebase at execution time (grep across all tiers + a unit test asserting none remain in the EF model after 6a).
- [x] `RiskGrouping` (kept — 1 use in `StatisticsService`), `FailedLoginAttempt`/`UserPassHistory` (removed — no live lockout/reuse logic), `UserPassReuseHistory` (kept — the live one) investigated; decisions recorded in CHANGELOG/ROADMAP.
- [x] Pass 6a: 23 dead entities unmapped + the orphan columns `risks.regulation`/`risks.project_id`, tables renamed `zz_deprecated_*`; nothing dropped. *(Implemented as one working-tree change ending in deletion; in a real phased rollout the entity classes would carry `[Obsolete]` for the observation release before 6b deletes them.)*
- [x] Observation release elapsed and logged in `schema_upgrade_log` before 6b (enforced by the `--phase 6b` gate; exercised in the integration test with an aged `6a` entry).
- [x] Pass 6b: the tool's automatic backup is taken **before** the drops; `--phase 6b` gate enforces `--yes` + the observation window (drops run under `FOREIGN_KEY_CHECKS=0`).
- [x] `risks.regulation`/`risks.project_id` dropped (6b). `risks.status` (old) intentionally **retained** — its `status_id` replacement must coexist a release; dropping it is a future phase, not this milestone.
- [x] `dotnet test src/netrisk.sln` green after the changes (unit + Testcontainers integration; 18 integration incl. the 3 new phase tests).
- [x] CHANGELOG + ROADMAP updated; numbered SQL is the applied path via the 6.1 tool (`--check`/`--dry-run` before a real apply).

## Testing Requirements

### Unit tests (no DB — EF model metadata + mocks)

- **Enum conversions (Phase 5):** assert `HasConversion` is configured for the `Risk` status enum and for `BiometricTransaction.TransactionResult` via model metadata; round-trip each enum value ↔ stored int through the configured converter.
- **Status value→enum mapping:** unit-test the function mapping each distinct legacy `risks.status` string to its enum/int, including the unmapped/unknown fallback.
- **Temporal mapping:** assert `created_at`/`updated_at` map to DATETIME with no auto-update behavior in the model.
- **Removal-candidate reference check:** a test (or analyzer) asserting that no `DbSet`/mapping references the deprecated entities after Pass 6a.

### Integration tests (local containers — real MySQL)

In `DAL.IntegrationTests` (`Testcontainers.MySql`, Docker local, `Category=Integration`).

- **Phase 5 — create-copy-coexist (core test):** seed `risks` rows spanning every distinct legacy `status` string; apply the Phase 5 migration; assert `status_id` is populated correctly per row, the **old `status` column still exists** (coexistence), and `Down()` drops `status_id` while preserving the original column.
- **Phase 5 — temporal types:** assert `created_at`/`updated_at` are DATETIME and that no `ON UPDATE CURRENT_TIMESTAMP` remains (an UPDATE that doesn't touch the column leaves it unchanged).
- **Pass 6a — deprecate is reversible:** apply 6a; assert candidate tables are renamed to `zz_deprecated_*` (present in `information_schema`, data intact) and the context no longer maps them; `Down()` restores original names.
- **Pass 6b — dump-before-drop ordering:** assert the per-table dump file is produced and verified **before** the `DropTable` executes; after 6b the `zz_deprecated_*` tables are gone.
- **Pass 6b — observation gate:** with a too-recent `6a` entry in `schema_upgrade_log`, assert the 6b run refuses; with an aged entry + `--yes`, it proceeds. (Reuses the 6.1 gate; here exercised against the real drop migration.)
- **Old-column drops:** after the coexistence/observation release, assert `risks.status` (old), `risks.regulation`, and `risks.project_id` (if confirmed dead in 6.3) are dropped and that dependent reads use the new columns.
- **Down round-trip:** every Phase 5 and Pass 6a migration restores prior state on `Down()`.

## Verification

- Unit + integration suites above pass; integration runs against the local Testcontainers MySQL.
- `--phase 5 --dry-run` impact report (status value distribution, row counts).
- 6b gate refusal/proceed behavior confirmed via the gate integration test.
- Full solution suite green after each pass.

## Dependencies & ordering

- **Depends on:** 6.3 (for `Risk.ProjectId`/`risks.status` removal decisions) and 6.1 (the `6b` gate + Phase 0 census).
- 6a and 6b are **separate releases** with at least one release of observation between them.
- Phase 5 and Phase 6 are their own releases.

```
6.1 ─► 6.2 ─► 6.3 ─► 6.4 Phase 5 ─► 6.4 Phase 6a ─► (≥1 release observation) ─► 6.4 Phase 6b
```

## Out of scope

- New security features (login lockout, password-reuse enforcement) — flagged here as a *decision input*, but implementing them is separate work (relevant to Track 7).
