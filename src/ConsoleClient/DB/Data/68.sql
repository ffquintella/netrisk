START TRANSACTION;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260611171952_Track6Phase1cCollationUtf8mb4', '10.0.9');

update settings SET value = '68' where name = 'db_version';

COMMIT;
