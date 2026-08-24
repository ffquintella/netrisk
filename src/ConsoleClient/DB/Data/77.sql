START TRANSACTION;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260824163207_Track3AspmSchema', '10.0.11');

-- Track 3 milestone 3.4.1 — the default SLA policy, seeded to the CISA benchmarks the spec cites:
-- criticals remediated in ~15 days, highs ~30, and a triage ladder of 2/5/10/15 days. entity_id is
-- NULL, so these are the global defaults every finding is measured against until an administrator
-- adds an entity override.
--
-- `severity` is NormalizedSeverity: 4 Critical, 3 High, 2 Medium, 1 Low. Informational findings
-- (0) get no row — an SLA on something nobody has to fix is noise.
--
-- effective_from is backdated to the epoch used elsewhere in this schema rather than the upgrade
-- moment, so findings that already exist resolve a policy instead of coming out with no due date.
INSERT INTO `sla_configurations`
    (`severity`, `max_triage_days`, `max_remediation_days`, `entity_id`, `effective_from`, `effective_to`, `created_at`)
VALUES
    (4,  2, 15, NULL, '1970-01-01 00:00:00', NULL, UTC_TIMESTAMP()),
    (3,  5, 30, NULL, '1970-01-01 00:00:00', NULL, UTC_TIMESTAMP()),
    (2, 10, 60, NULL, '1970-01-01 00:00:00', NULL, UTC_TIMESTAMP()),
    (1, 15, 90, NULL, '1970-01-01 00:00:00', NULL, UTC_TIMESTAMP());

-- Track 3 milestone 3.3.1 — the per-scanner dedup defaults.
--
-- Scanners that publish a stable per-finding id of their own lead with UniqueIdFromTool, because
-- the tool's own promise of stability beats any fingerprint we can compute; everything else falls
-- through to HashBased. LegacyHashCode is appended for nessus alone: that is the only importer with
-- pre-existing data in the field, and without it a re-import of an already-imported .nessus file
-- would fail to match its own earlier rows and duplicate the whole register once.
--
-- auto_close_missing is 0 everywhere. Turning it on is a deliberate per-scanner decision, and the
-- default has to be the one that cannot silently close findings a partial scan did not reach.
INSERT INTO `scanner_dedup_configurations`
    (`importer`, `strategy_chain`, `hash_fields`, `auto_close_missing`, `created_at`)
VALUES
    ('nessus',     'HashBased,LegacyHashCode',    'tool,ruleId,asset,location,cve', 0, UTC_TIMESTAMP()),
    ('sarif',      'UniqueIdFromTool,HashBased',  'tool,ruleId,location',           0, UTC_TIMESTAMP()),
    ('semgrep',    'UniqueIdFromTool,HashBased',  'tool,ruleId,location',           0, UTC_TIMESTAMP()),
    ('zap',        'HashBased',                   'tool,ruleId,asset,location',     0, UTC_TIMESTAMP()),
    ('trivy',      'HashBased',                   'tool,ruleId,location,cve',       0, UTC_TIMESTAMP()),
    ('openvas',    'HashBased',                   'tool,ruleId,asset,location',     0, UTC_TIMESTAMP()),
    ('burp',       'HashBased',                   'tool,ruleId,asset,location',     0, UTC_TIMESTAMP()),
    ('snyk',       'UniqueIdFromTool,HashBased',  'tool,ruleId,location,cve',       0, UTC_TIMESTAMP()),
    ('grype',      'HashBased',                   'tool,ruleId,location,cve',       0, UTC_TIMESTAMP()),
    ('dependabot', 'UniqueIdFromTool,HashBased',  'tool,ruleId,location,cve',       0, UTC_TIMESTAMP());

update settings SET value = '77' where name = 'db_version';

COMMIT;
