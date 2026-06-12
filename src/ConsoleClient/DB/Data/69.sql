START TRANSACTION;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260612115319_Track6Phase3Relationships', '10.0.7');

update settings SET value = '69' where name = 'db_version';

COMMIT;
