START TRANSACTION;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241116011937_newIndexActions', '8.0.10');

update settings SET value = '33' where name = 'db_version';

COMMIT;