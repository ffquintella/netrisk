# Milestone 6.3 — Relationships & Indexing for Performance

**Track:** 6 — Database Uniformization & Schema Health
**Plan mapping:** [docs/plano-uniformizacao-banco.md](../../docs/plano-uniformizacao-banco.md) — *Fase 3* + *Fase 4*
**Status:** Completed (Phase 3 → `db_version` 69, Phase 4 → `db_version` 70)
**Risk:** Medium (Phase 3 — orphan cleanup nulls values) to low-medium (Phase 4 — BLOB→TEXT needs encoding validation)

> **Implementation notes (delivered):**
> - `Risk.Owner/Manager/SubmittedBy`, `FrameworkControl.ControlOwner`, `FrameworkControlTest.Tester` → nullable FKs to `user(value)` with `ON DELETE SET NULL` + navigations. `FrameworkControl.Tester` does **not** exist (plan-doc drift) — only `FrameworkControlTest.Tester`.
> - `Incident.ReportedBy`: added `reported_by_id` FK, kept the free-text column, best-effort name backfill (chosen approach).
> - `Risk.ProjectId`: **no FK** — no live `projects` table exists; flagged a Milestone 6.4 removal candidate.
> - `IncidentToIncidentResponsePlan`: dead commented mapping removed; the join is canonical via `UsingEntity`.
> - Orphans logged to a new `schema_upgrade_orphans` table **before** nulling (the recovery record).
> - Phase 4 BLOB→text: `user.email` → `varchar(255)` (direct, app UTF-8); legacy `frameworks`/`framework_controls`/`permissions` text BLOBs → `TEXT` via a **`latin1`→`utf8mb4` round-trip** (they hold cp1252 seed bytes — a direct convert errors). `permissions.description` JSON output changes base64 → plain text. C# `byte[]` → `string` across entity + MapsterConfiguration + the few call sites.
> - Indexes added only where a real Sieve filter/sort or listing query justifies them; redundant `framework_control_tests.id` UNIQUE index removed.
> - Verified: `Track6Phase3Tests` + `Track6Phase4Tests` (Testcontainers MariaDB) and `Track6RelationshipModelTests` (DB-free EF metadata) — all green, full unit + integration suites pass.

## Goal

Turn every correlation column into a real, navigable, indexed foreign key, then index the hot filter/sort columns validated against actual service and Sieve queries. No data is destroyed — orphan cleanup only nulls dangling references, and every nulled row is logged first.

## Part A — Phase 3: FKs and explicit relationships

**Mandatory pre-step before each constraint:** clean orphans, or `ALTER TABLE … ADD CONSTRAINT` fails. Always write an orphan report (SELECT → log table / CSV) *before* nulling, so nothing is destroyed without a record:

```sql
-- example: risks.owner with no matching user
UPDATE risks r LEFT JOIN user u ON r.owner = u.value
SET r.owner = NULL WHERE u.value IS NULL AND r.owner IS NOT NULL;
```

The `database upgrade-schema --phase 3 --dry-run` impact report (from 6.1) must surface the orphan counts per column before execution.

1. **`Risk.Owner`, `Risk.Manager`, `Risk.SubmittedBy` → FK to `user`.** Make nullable where it makes sense, `ON DELETE SET NULL`. Configure `HasOne` in EF with navigations (`Risk.OwnerUser`, `Risk.ManagerUser`, `Risk.SubmittedByUser`).
2. **`FrameworkControl.ControlOwner`, `FrameworkControl.Tester`, `FrameworkControlTest.Tester` → FK to `user`** with navigations.
3. **`Risk.ProjectId`:** decision point — if no live projects table exists, mark it a Milestone 6.4 removal candidate; if one exists, add the FK.
4. **`Incident.ReportedBy` (free varchar):** product decision — either introduce `reported_by_id` FK to `user` while keeping the text column for external reporters, or leave it free and document it. Data migration: best-effort match the text against `user.name` to populate the new FK; **nothing is deleted**.
5. **Resolve the `IncidentToIncidentResponsePlan` join table** (fluent mapping currently commented while the relation appears active via `UsingEntity`): either map the entity explicitly or consolidate on `UsingEntity` — remove the ambiguity. Confirm the real state first (output of the 6.1 / Phase 0 snapshot-divergence check).

**Mitigation for the medium risk:** orphan report persisted and reviewed before the nulling UPDATE runs; `Down()` cannot un-null, so the orphan log *is* the recovery record (alongside the mandatory pre-migration backup from 6.1).

## Part B — Phase 4: Indexing for performance

1. **Index every FK column** created in Phase 3 (MySQL usually creates an index with the constraint — verify each).
2. **Hot filter/sort columns** without an index. Candidates for this domain, to be **validated against the real `ServerServices` queries and Sieve filters** (`ApplicationSieveProcessor` maps `Vulnerability` and `Host` — prioritize those):
   - `risks.status` + `submission_date` (composite)
   - `vulnerabilities.status` / `first_detection` / `last_detection` / `host_id`
   - `comments(type, taggee_id)`
   - `nr_actions(object_type, object_id)`
   - `audit(DateTime)`
3. **Remove redundant/useless indexes** — e.g. a UNIQUE index redundant with the PK on `framework_control_tests.id`; low-selectivity boolean-only indexes that never appear alone in a WHERE.
4. **BLOB → correct type.** Convert BLOB columns that hold text to `varchar`/`TEXT`: `framework.name`, `framework.description`, `user.email`, `permission.name`, `permission.description`, `risk_catalog.description`, etc. `ALTER … MODIFY` converts in-place without loss and enables correct collation and indexes/FULLTEXT.
5. **Measure.** Enable slow query log / capture `EXPLAIN` on the listing-service queries before and after, to evidence the gain.

**Why low-medium risk:** BLOB→TEXT conversions require encoding validation — test on a DB clone before applying.

## Acceptance criteria

- [ ] Orphan report written and reviewed for every correlation column **before** any nulling UPDATE; report retained as an artifact.
- [ ] FK constraints + EF navigations added for `Risk.Owner/Manager/SubmittedBy`, `FrameworkControl.ControlOwner/Tester`, `FrameworkControlTest.Tester`; `ON DELETE SET NULL` where columns are nullable.
- [ ] `Risk.ProjectId` resolved (FK added, or flagged for 6.4 removal with rationale recorded).
- [ ] `Incident.ReportedBy` decision made, documented, and implemented; if FK added, the text-match backfill ran and is non-destructive.
- [ ] `IncidentToIncidentResponsePlan` mapping is unambiguous (single canonical representation) and matches the live DB.
- [ ] Every Phase-3 FK column is indexed.
- [ ] Hot-column indexes added only where justified by an observed query/Sieve filter; each addition references the query it serves.
- [ ] Redundant indexes removed; `EXPLAIN`/slow-log evidence captured before/after for the prioritized listing queries.
- [ ] All listed BLOB-as-text columns converted to `varchar`/`TEXT`, validated on a clone for encoding integrity first.
- [ ] `Down()` works for every migration.
- [ ] `dotnet test src/netrisk.sln` green (assert both list and total-count tuple values for affected paged queries per [src/AI_TESTING_INSTRUCTIONS.md](../../src/AI_TESTING_INSTRUCTIONS.md)); GUIClient smoke passes for risks, framework controls, incidents, vulnerabilities, and hosts.
- [ ] CHANGELOG updated; applied to homolog via the 6.1 tool with `--dry-run` attached before prod.

## Testing Requirements

### Unit tests (no DB — EF model metadata + mocks)

- **Navigations & FK config (Phase 3):** assert via EF model metadata that `Risk.OwnerUser`/`ManagerUser`/`SubmittedByUser`, `FrameworkControl.ControlOwner`/`Tester`, and `FrameworkControlTest.Tester` are configured as `HasOne` relationships to `user` with the expected `DeleteBehavior` (`SetNull`) and nullability.
- **Join-table resolution:** assert `IncidentToIncidentResponsePlan` is represented exactly once in the model (no ambiguous duplicate mapping).
- **Sieve/service query behavior:** for the affected paged list endpoints, assert **both the list and the total-count tuple** values against `MockDbContext` per [src/AI_TESTING_INSTRUCTIONS.md](../../src/AI_TESTING_INSTRUCTIONS.md) — guards that added navigations don't change result shape.
- **`ReportedBy` backfill mapping:** unit-test the text→`user.name` match function used to populate `reported_by_id` (matches, no-match, ambiguous).

### Integration tests (local containers — real MySQL)

In `DAL.IntegrationTests` (`Testcontainers.MySql`, Docker local, `Category=Integration`).

- **Orphan cleanup then constraint (core test):** seed `risks` with valid owners **and** dangling owner values with no matching `user`; run the Phase 3 migration; assert (a) an orphan report was captured **before** nulling, (b) the dangling values are now `NULL`, valid ones untouched, and (c) the FK constraint exists in `information_schema.table_constraints`.
- **Constraint-fails-without-cleanup guard:** assert that applying the `ADD CONSTRAINT` against un-cleaned orphan data fails — proving the cleanup step is mandatory and correctly ordered.
- **`ON DELETE SET NULL` behavior:** delete a referenced `user` row; assert the referencing FK column is set to `NULL`, not cascaded/blocked.
- **FK columns indexed:** assert every Phase 3 FK column has an index in `information_schema.statistics`.
- **Hot-column indexes (Phase 4):** assert each added index exists; for at least the Sieve-backed `vulnerabilities`/`hosts` queries, capture `EXPLAIN` before/after and assert the index is used (no full table scan on the indexed predicate).
- **Redundant index removal:** assert the redundant UNIQUE-on-PK index (`framework_control_tests.id`) is gone.
- **BLOB→TEXT round-trip (encoding):** seed multi-byte/UTF-8 text into each converted column (`framework.name`, `user.email`, `permission.*`, `risk_catalog.description`, …) **before** conversion; apply the `MODIFY`; assert the column type is `varchar`/`TEXT`, collation is correct, and the content reads back byte-identical.
- **Down round-trip:** `Up()`→`Down()` restores the prior schema; note that the orphan-nulling is not reversible by `Down()` (the orphan log is the recovery record) — assert the report artifact exists.

## Verification

- Unit + integration suites above pass; integration runs against the local Testcontainers MySQL.
- Pre-execution orphan/impact report via `--phase 3 --dry-run`.
- Before/after `EXPLAIN` on the Sieve-backed `Vulnerability`/`Host` list queries.
- Full solution suite green, with paged-query tuple assertions.

## Dependencies & ordering

- **Depends on:** 6.2 (uniform naming) and 6.1 (tool).
- **Blocks:** 6.4 (`Risk.ProjectId` / `risks.status` removal decisions feed Phase 6).
- Phases 3 and 4 may be grouped into one release.

## Out of scope

- Temporal/status type standardization and enum conversion (6.4).
- Dropping the deprecated tables/columns (6.4).
