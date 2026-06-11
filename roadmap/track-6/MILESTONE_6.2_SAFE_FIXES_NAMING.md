# Milestone 6.2 — Safe Fixes & Naming Uniformization

**Track:** 6 — Database Uniformization & Schema Health
**Plan mapping:** [docs/plano-uniformizacao-banco.md](../../docs/plano-uniformizacao-banco.md) — *Fase 1* + *Fase 2*
**Status:** Not started
**Risk:** Very low (Phase 1 — non-semantic fixes) to low (Phase 2 — atomic renames, no drops)

## Goal

Apply the low-risk corrections and converge naming onto `snake_case`, using **renames only — no drops, no data migration**. The C# layer stays PascalCase throughout: every rename is absorbed by `ToTable`/`HasColumnName` mapping in `NRDbContext`, so **no DTO, service, REST client, or GUI binding changes**.

## Context

- The DB carries three naming conventions: ~95 legacy snake_case tables (SimpleRisk heritage), 8 newer PascalCase tables, and hybrids (`vulnerabilities_to_actions` with camelCase columns, `reports` with `creationDate`/`creatorId`).
- snake_case is the standard for ~92% of tables, so renaming the 8 newcomers is far cheaper than renaming 95 legacy tables.
- Applied through `database upgrade-schema --phase 1` and `--phase 2` (built in 6.1).

## Part A — Phase 1: Safe fixes (one small, fully reversible migration)

1. **Invalid `0000-00-00` defaults → NULL.** Change the columns to `NULL DEFAULT NULL` and update existing `0000-00-00` rows to `NULL`. Targets: `mgmt_reviews.next_review`, `mitigations.last_update`, `client_registration.last_verification_date`, `client_registration.registration_date`. (These break under MySQL strict mode.)
2. **Index typo fixes** (drop+create, touches no data): `idx_biometic_id` → `idx_biometric_transaction_id`, `idx_biometic_anchor` → corrected spelling, `idx_irpt_sequencial` → `…_sequential`, `idx_irpt_optinal` → `…_optional`.
3. **Boolean normalization** `tinyint(4)` → `tinyint(1)`: `comments.is_anonymous`, `framework_controls.deleted`, and `failed_login_attempts.expired` *if it survives Milestone 6.4*.
4. **Collation/charset:** standardize on `utf8mb4_unicode_ci` where there is a mix.

**Why low risk:** none of these change meaning. Defaults move from an illegal sentinel to NULL; index renames and boolean width are cosmetic; collation is unified.

## Part B — Phase 2: Naming uniformization (renames, zero data loss)

> **Critical authoring note:** generate the migration with `RenameTable`/`RenameColumn` so EF emits `RENAME` (preserving data). EF sometimes scaffolds `Drop`+`Create` instead — **review the generated migration and rewrite by hand to `Rename`** if so. Verify with `--dry-run` / `migrationScript.sh` before applying.

1. **PascalCase tables → snake_case (8):**
   - `Incidents` → `incidents`
   - `IncidentResponsePlans` → `incident_response_plans`
   - `IncidentResponsePlanTasks` → `incident_response_plan_tasks`
   - `IncidentResponsePlanExecutions` → `incident_response_plan_executions`
   - `IncidentResponsePlanTaskExecutions` → `incident_response_plan_task_executions`
   - `IncidentToIncidentResponsePlan` → `incident_to_incident_response_plan`
   - `FaceIDUsers` → `face_id_users`
   - `BiometricTransaction` → `biometric_transactions`
   - `FixRequest` → `fix_requests`
2. **PascalCase/camelCase columns → snake_case** in the renamed and hybrid tables:
   - `vulnerabilities_to_actions.actionId` → `action_id`, `vulnerabilityId` → `vulnerability_id`
   - `reports.creationDate` → `created_at`, `creatorId` → `creator_id`, `fileId` → `file_id`
   - `hosts.FQDN` → `fqdn`, `OS` → `os`
   - `messages.Message` → `message`
3. **Update the EF mapping** (`ToTable` / `HasColumnName`) in `NRDbContext` so C# entity and DTO names are unchanged.
4. **Constraint/index names:** MySQL keeps FKs across a table rename but leaves constraint names in the old style — rename them to the `fk_<table>_<column>` convention as part of the same migration.
5. *(Optional, separate pass — not required for this milestone)* pluralize singular legacy tables (`user` → `users`, `team` → `teams`, `audit` → `audits`). High churn, low gain — do it only if explicitly desired, one migration per group.

**Why low risk:** renames are atomic in MySQL. The one watch-point is keeping the EF snapshot and the live DB in sync — guarded by the test suite and a confirmation `migrationScript`.

## Acceptance criteria

- [ ] Phase 1 ships as a single reversible migration; no `0000-00-00` values or defaults remain on the four named columns; the four index typos are corrected; the listed booleans are `tinyint(1)`; mixed collations are unified to `utf8mb4_unicode_ci`.
- [ ] Phase 2 renames all 8 tables and all listed columns via `RENAME` (verified in the generated SQL — no `Drop`/`Create` of the renamed objects).
- [ ] Row counts on every renamed table are identical before and after (asserted by the 6.1 post-apply validation).
- [ ] `NRDbContext` mappings updated; **no** C# entity/DTO/service/client/GUI rename anywhere (diff confined to `DAL` + migrations).
- [ ] FK/index constraint names follow `fk_<table>_<column>` / `idx_<table>_<columns>` on the touched objects.
- [ ] Both phases have a working `Down()`.
- [ ] `dotnet test src/netrisk.sln` green; GUIClient smoke test passes (incidents, IRP, reports, hosts, vulnerabilities-to-actions screens load and round-trip).
- [ ] CHANGELOG updated; both phases applied to homolog via the 6.1 tool with the `--dry-run` attached to the change record before prod.

## Testing Requirements

### Unit tests (no DB — EF model metadata + mocks)

- **Mapping assertions (Phase 2):** for each renamed entity, assert via EF model metadata (`IEntityType.GetTableName()` / property `GetColumnName()`) that it now maps to the snake_case table/column name (`incidents`, `incident_response_plans`, `fix_requests`, `action_id`, `created_at`, `fqdn`, `os`, `message`, …) — proves the `ToTable`/`HasColumnName` mapping changed while C# names did not.
- **No C# rename regression:** assert the entity/DTO type names are unchanged (the diff is mapping-only); existing `ServerServices`/`ClientServices` tests over the affected entities still resolve and pass against `MockDbContext`.
- **Boolean mapping (Phase 1):** assert the targeted boolean properties map to a single-bit/bool column type in the model.

### Integration tests (local containers — real MySQL)

In `DAL.IntegrationTests` (`Testcontainers.MySql`, Docker local, `Category=Integration` trait). Each test starts from a schema at the pre-phase migration, seeds rows, applies the phase migration, and asserts.

- **Phase 1 — defaults:** seed rows with `0000-00-00` in `mgmt_reviews.next_review`, `mitigations.last_update`, `client_registration.*`; apply migration; assert those values are now `NULL` and the column default is `NULL`. Assert `Down()` is functional.
- **Phase 1 — index typos:** assert via `information_schema.statistics` that `idx_biometric_transaction_id`, `…_sequential`, `…_optional` exist and the misspelled names are gone.
- **Phase 1 — boolean width & collation:** assert target columns are `tinyint(1)` and the unified collation is `utf8mb4_unicode_ci` in `information_schema.columns`.
- **Phase 2 — rename preserves data (core test):** seed N rows in each PascalCase table and hybrid column, apply the rename migration, then assert (a) the snake_case table/column names exist in `information_schema`, (b) the old names are gone, and (c) **row counts and a content checksum are identical** — proving `RENAME` not `Drop`/`Create`.
- **Phase 2 — generated SQL shape:** assert the migration's operations are `RenameTable`/`RenameColumn` (inspect the EF migration operations), not `Drop`+`Create`.
- **Phase 2 — FK survival & constraint naming:** assert foreign keys on renamed tables still exist and have been renamed to the `fk_<table>_<column>` convention.
- **Down round-trip:** apply `Up()` then `Down()` for both phases; assert the schema returns to the pre-phase state with data intact.

## Verification

- Unit + integration suites above pass; integration runs against the local Testcontainers MySQL.
- `--dry-run` review of generated SQL for both phases (confirm RENAME, confirm no destructive ops).
- Full solution suite green.

## Dependencies & ordering

- **Depends on:** 6.1 (tool + Phase 0 baseline).
- **Blocks:** 6.3 (FK work assumes uniform naming).
- Phases 1 and 2 may be grouped into one release.

## Out of scope

- Adding FK constraints / relationships (6.3).
- Type changes beyond boolean width and collation (6.4).
- Dropping anything (6.4).
- Renaming the 95 legacy snake_case tables to plural (optional, deferred).
