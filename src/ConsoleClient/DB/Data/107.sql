START TRANSACTION;

-- M53, T247 (S40 §7.6, §9.3) -- the snapshot job's tables seed nothing: the job fills them from the first
-- snapshot after a profile is in force. Pure DML: a CREATE or ALTER here would commit the transaction out
-- from under the rest, and the db_version bump below would stop being the commit point.

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261016162414_RiskIndexSnapshotJobTables', '10.0.13')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

update settings SET value = '107' where name = 'db_version';

COMMIT;
