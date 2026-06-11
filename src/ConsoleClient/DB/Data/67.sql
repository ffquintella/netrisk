START TRANSACTION;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260611170136_Track6Phase2bIsAnonymousColumnRename', '10.0.7');

update settings SET value = '67' where name = 'db_version';

COMMIT;
