# Milestone 6.1 — Upgrade Tooling & Preparation

**Track:** 6 — Database Uniformization & Schema Health
**Plan mapping:** [docs/plano-uniformizacao-banco.md](../../docs/plano-uniformizacao-banco.md) — *Ferramenta de aplicação* + *Fase 0*
**Status:** Not started
**Risk:** None (no schema changes — tooling, baselines, and documentation only)

## Goal

Build the safety net and the delivery mechanism before any schema is touched. This milestone produces (a) a dedicated, auditable upgrade command in the ConsoleClient that every later phase will be applied through, (b) a frozen production baseline (backup, row-count census, snapshot-divergence report), and (c) a documented target naming convention so new entities are born compliant. Nothing in 6.2–6.4 may be applied to a real environment until 6.1 ships, because those phases are *driven* by this tool.

## Context

- The ConsoleClient already exposes `database status|init|update|backup|restore|fixData` via `DatabaseCommand` ([src/ConsoleClient/Commands/DatabaseCommand.cs](../../src/ConsoleClient/Commands/DatabaseCommand.cs)) over `IDatabaseService` in `ServerServices`. We **extend** this surface; we do not build a new tool.
- Migration version metadata lives in [src/ConsoleClient/DB/DatabaseInformation.yaml](../../src/ConsoleClient/DB/DatabaseInformation.yaml) (`InitialVersion` / `TargetVersion`), loaded by `GetDatabaseInformation()`.
- EF migrations live in `DAL`, applied with `ConsoleClient` as the startup project against the `NRDbContext` context (see [CLAUDE.md](../../CLAUDE.md)).

## Deliverables

### D1 — `database upgrade-schema` command

Extend `DatabaseSettings` / `DatabaseCommand` with a new operation:

```
netrisk-console database upgrade-schema --phase <n> [--env homolog|prod] [--dry-run] [--check] [--yes]
```

| Stage | Behavior |
|---|---|
| `--check` | Pre-flight only, mutates nothing: DB connectivity, MySQL server version, current migration vs. the migration the phase expects as its starting point, free disk space for a backup, and schema-vs-`ModelSnapshot` divergence. Exits non-zero if any check fails. |
| `--dry-run` | Emits the exact SQL for the phase (equivalent to `migrationScript.sh` between the current and target migration) plus an **impact report**: row counts of affected tables, orphan rows that would be nulled (Phase 3), and tables that would be deprecated/dropped (Phase 6). Output is written to a file suitable for attaching to a change request. |
| execution | Mandatory ordered sequence: **automatic backup** (reuse `DatabaseService.Backup()`) → orphan/census report written to a log table → apply the phase's EF migrations (`Database.Migrate()` up to the phase's target migration) → **post-apply validations** (row counts match the pre-flight on renamed tables, FKs valid, expected indexes present) → append a row to `schema_upgrade_log` (phase, target version, timestamp, operator, backup hash). |
| failure | Any failed post-apply validation prints a **restore instruction** pointing at the just-created backup. Restore is **not** automatic in production — it is the operator's decision. |
| `--phase 6b` | Extra gate (destructive): refuses to run unless phase `6a` has been recorded in `schema_upgrade_log` for at least N days (the observation cycle), and requires an explicit `--yes`. Before any `DropTable`, it produces per-table `mysqldump --tables` dumps and only drops after verifying the dump file exists. |

**Environment selection.** `--env` selects the connection profile (`appsettings.Homolog.json` / `appsettings.Production.json`, or the matching secret) on top of the existing user-secrets/appsettings resolution. Production requires interactive confirmation by typing the database name, unless `--yes --env prod` is supplied (pipeline use).

### D2 — Phase manifest (data-driven)

A versioned manifest checked into the repo at `src/ConsoleClient/DB/SchemaUpgradePhases.yaml` (alongside the existing `DatabaseInformation.yaml`) lists, **per phase**:

- the target EF migration name,
- the post-apply validations to run,
- the census/orphan SQL scripts to execute and where their output is logged.

The command is driven entirely by this manifest, so adding a future phase is: add a manifest entry + its migration. No new command code.

### D3 — `schema_upgrade_log` audit table

A new EF-mapped table recording every upgrade execution: `id`, `phase`, `target_version`, `applied_at` (UTC DATETIME), `operator`, `environment`, `backup_path`, `backup_hash`, `status`, `notes`. Created by its own migration (this is the *only* schema change in 6.1, and it is purely additive). The `--phase 6b` gate reads from this table to enforce the observation window.

### D4 — Production baseline (Plan Phase 0)

A one-time, recorded baseline of the current production database — no schema change:

1. **Full dump** of production; generate the current migration's SQL script (`migrationScript.sh`) as a reference artifact.
2. **Row-count census** (`SELECT COUNT(*)`) of every Phase-6 removal candidate (the ~24 tables + orphan columns listed in the plan). A candidate *with* data is treated differently (archive) than an empty one (drop).
3. **Schema-vs-snapshot divergence check** — compare the live DB against `ModelSnapshot`. Specifically confirm the real state of `IncidentToIncidentResponsePlan` (the fluent mapping is commented in the context but the relation appears active via `UsingEntity`).
4. Capture all three as artifacts under `docs/security/`-style storage or attach to the milestone ticket.

### D5 — Convention documentation

Document the target naming convention so new entities are born compliant, in [CLAUDE.md](../../CLAUDE.md) and/or [docs/plano-uniformizacao-banco.md](../../docs/plano-uniformizacao-banco.md):

| Item | Standard |
|---|---|
| Tables | `snake_case`, plural (`incidents`, `incident_response_plans`) |
| Columns | `snake_case` (`created_at`, `assigned_to_id`) |
| FKs | column `<entity>_id` + constraint `fk_<table>_<column>` + configured EF relationship |
| Indexes | `idx_<table>_<columns>`; unique `uq_…`; fulltext `ftx_…` |
| Temporal | `created_at` DATETIME NOT NULL; `updated_at` DATETIME NULL — always UTC, no auto-update TIMESTAMP |
| Booleans | `tinyint(1)` |
| Status/enums | `int` + C# enum with explicit conversion |
| Text | `varchar(n)` when bounded; `TEXT`/`LONGTEXT` when free; **never** BLOB for text |

## Acceptance criteria

- [ ] `database upgrade-schema` exists with `--check`, `--dry-run`, `--env`, `--phase`, and `--yes`, and rejects unknown phases.
- [ ] `--check` and `--dry-run` are provably side-effect-free (verified against a clone).
- [ ] Execution path performs backup → census → migrate → validate → log in that order, and aborts before migrating if the backup step fails.
- [ ] A failed post-apply validation prints an actionable restore instruction and exits non-zero **without** auto-restoring in prod.
- [ ] `SchemaUpgradePhases.yaml` exists and the command reads its phase→migration mapping and validations from it.
- [ ] `schema_upgrade_log` table is created by an additive migration and populated on every execution.
- [ ] Production baseline artifacts (dump reference, removal-candidate census, snapshot-divergence report) are captured and stored.
- [ ] Target naming convention is documented where new-entity authors will see it.
- [ ] `dotnet test src/netrisk.sln` is green; new tests cover the manifest parsing, the pre-flight checks, and the `6b` observation-window gate (using `MockDalService`/`MockDbContext` per [src/AI_TESTING_INSTRUCTIONS.md](../../src/AI_TESTING_INSTRUCTIONS.md)).

## Verification

- Unit tests for manifest deserialization, pre-flight check logic, and the `6b` gate.
- Smoke test: run `--check` and `--dry-run` against a throwaway clone and confirm zero mutation + correct SQL/impact output.
- Run the full suite green as the Phase 0 baseline requirement.

## Dependencies & ordering

- **Blocks:** 6.2, 6.3, 6.4 (all are applied *through* this tool).
- **Depends on:** nothing.

## Out of scope

- Any table/column rename, FK, index, type change, or drop (those are 6.2–6.4).
- Automatic restore in production.
