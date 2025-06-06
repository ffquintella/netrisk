START TRANSACTION;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20250606113540_biometricTransactionIdIndex', '9.0.3');

update settings SET value = '61' where name = 'db_version';

COMMIT;