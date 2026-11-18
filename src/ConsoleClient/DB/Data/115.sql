START TRANSACTION;

-- M58, T281-T282 (S39 D-14, D-18, D-20, D-21). Pure DML: the db_version bump below is the commit point.
-- Nothing is backfilled (docs/features/host-entity-assignment.md): last_assessed_at waits for the next scan, an unknown
-- entity_source ranks 0, and a NULL repository keeps every existing finding's dedup key exactly as it was.

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261118164710_HostAssessmentAndEntityAttribution', '10.0.13')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

update settings SET value = '115' where name = 'db_version';

COMMIT;
