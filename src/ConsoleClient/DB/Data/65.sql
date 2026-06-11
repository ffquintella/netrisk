START TRANSACTION;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260611163225_Track6Phase2NamingUniformization', '10.0.7');

update settings SET value = '65' where name = 'db_version';

COMMIT;
