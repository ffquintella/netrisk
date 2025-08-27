START TRANSACTION;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20250606112447_biometricTransactionsDetailing', '9.0.3');

update settings SET value = '60' where name = 'db_version';

COMMIT;