START TRANSACTION;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260618000000_AddProcessedSyncActions', '10.0.7');

update settings SET value = '75' where name = 'db_version';

COMMIT;
