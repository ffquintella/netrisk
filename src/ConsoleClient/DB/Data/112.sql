START TRANSACTION;

-- M56, T265-T267 (S40 §7.6) -- no rows to seed: a Tenable connection is created by an administrator, and vendor_scores is
-- filled by its first sync with "collect ACR" or "collect AES" on. IntegrationKind.Tenable = 7 and
-- IntegrationSyncStatus.Queued = 5 are enum values without DDL (S40 §7.3).
-- Pure DML: a CREATE or ALTER here would commit the transaction out from under the rest, and the db_version bump below
-- would stop being the commit point.

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261118135340_TenableIntegration', '10.0.13')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

update settings SET value = '112' where name = 'db_version';

COMMIT;
