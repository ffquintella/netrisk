START TRANSACTION;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260611141630_SchemaUpgradeLog', '10.0.7');

update settings SET value = '63' where name = 'db_version';

COMMIT;
