START TRANSACTION;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260611164504_Track6Phase1bBooleanNormalization', '10.0.7');

update settings SET value = '66' where name = 'db_version';

COMMIT;
