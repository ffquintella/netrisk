START TRANSACTION;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260612124024_Track6Phase5StatusTypeStandardization', '10.0.7');

update settings SET value = '71' where name = 'db_version';

COMMIT;
