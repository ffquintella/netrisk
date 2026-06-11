START TRANSACTION;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260611161327_Track6Phase1SafeFixes', '10.0.7');

update settings SET value = '64' where name = 'db_version';

COMMIT;
