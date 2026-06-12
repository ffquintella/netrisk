START TRANSACTION;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260612120758_Track6Phase4IndexingBlobText', '10.0.7');

update settings SET value = '70' where name = 'db_version';

COMMIT;
