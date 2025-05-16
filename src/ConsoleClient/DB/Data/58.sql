START TRANSACTION;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20250515171306_FaceIDUser', '9.0.3');

update settings SET value = '58' where name = 'db_version';

COMMIT;