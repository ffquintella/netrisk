START TRANSACTION;

-- M53, T250 (S40 §7.2, §7.6) -- the bridges and the preview requests seed nothing: the snapshot job writes a bridge on the
-- first day of a newly active profile, and the API queues a preview on request. Pure DML: a CREATE or ALTER here would
-- commit the transaction out from under the rest, and the db_version bump below would stop being the commit point.

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261016172322_RiskIndexBridgesAndPreviews', '10.0.13')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

update settings SET value = '108' where name = 'db_version';

COMMIT;
