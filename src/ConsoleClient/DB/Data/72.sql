START TRANSACTION;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260612124411_Track6Phase6aDeprecateDeadTables', '10.0.7');

update settings SET value = '72' where name = 'db_version';

COMMIT;
