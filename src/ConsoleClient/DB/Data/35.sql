START TRANSACTION;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241120184132_IRPDBAdjustment', '8.0.10');

update settings SET value = '35' where name = 'db_version';

COMMIT;

