START TRANSACTION;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20250516110055_BiometricTransactions', '9.0.3');

update settings SET value = '59' where name = 'db_version';

COMMIT;