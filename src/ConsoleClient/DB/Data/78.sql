START TRANSACTION;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260824181704_SyncActionIdVarchar', '10.0.11');

update settings SET value = '78' where name = 'db_version';

COMMIT;
