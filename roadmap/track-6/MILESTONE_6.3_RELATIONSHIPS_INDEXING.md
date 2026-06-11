# Milestone 6.3 — Relationships & Indexing for Performance

**Track:** 6 — Database Uniformization & Schema Health
**Plan mapping:** [docs/plano-uniformizacao-banco.md](../../docs/plano-uniformizacao-banco.md) — *Fase 3* + *Fase 4*
**Status:** Not started
**Risk:** Medium (Phase 3 — orphan cleanup nulls values) to low-medium (Phase 4 — BLOB→TEXT needs encoding validation)

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

## Verification

- Pre-execution orphan/impact report via `--phase 3 --dry-run`.
- Post-apply FK validity and index-presence checks (6.1 post-apply validations).
- BLOB→TEXT round-trip and encoding check on a clone.
- Before/after `EXPLAIN` on the Sieve-backed `Vulnerability`/`Host` list queries.
- Full test suite, with paged-query tuple assertions.

## Dependencies & ordering

- **Depends on:** 6.2 (uniform naming) and 6.1 (tool).
- **Blocks:** 6.4 (`Risk.ProjectId` / `risks.status` removal decisions feed Phase 6).
- Phases 3 and 4 may be grouped into one release.

## Out of scope

- Temporal/status type standardization and enum conversion (6.4).
- Dropping the deprecated tables/columns (6.4).
