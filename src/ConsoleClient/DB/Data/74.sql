START TRANSACTION;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260616171828_AddUserEntityRoles', '10.0.7');

update settings SET value = '74' where name = 'db_version';

COMMIT;
