START TRANSACTION;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260824124133_AddIrpTaskDependenciesAndOverride', '10.0.7');

update settings SET value = '76' where name = 'db_version';

COMMIT;
