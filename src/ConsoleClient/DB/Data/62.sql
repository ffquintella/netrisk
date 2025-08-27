START TRANSACTION;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20250612190206_UpdateBiometricTransactions', '9.0.3');

update settings SET value = '62' where name = 'db_version';

COMMIT;
