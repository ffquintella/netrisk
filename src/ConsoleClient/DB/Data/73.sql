START TRANSACTION;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260612124518_Track6Phase6bDropDeprecatedTables', '10.0.7');

update settings SET value = '73' where name = 'db_version';

COMMIT;
