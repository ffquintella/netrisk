START TRANSACTION;

-- M57, T270-T276 (S40 §7.6) -- no rows to seed: the new sync phases are off until an administrator turns them on, the
-- history backfill runs on the next SecurityScorecard sync, and next_due_at is anchored by the first scheduled tick.
-- hosts.criticality_source stays NULL (unknown) for existing rows: who wrote a criticality before this version is not
-- recorded anywhere, and guessing it from external_provider would claim a precedence nobody established.
-- Pure DML: a CREATE or ALTER here would commit the transaction out from under the rest, and the db_version bump below
-- would stop being the commit point.

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261118160000_VendorPostureExpansion', '10.0.13')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

update settings SET value = '113' where name = 'db_version';

COMMIT;
