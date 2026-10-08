START TRANSACTION;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261008160000_ConcurrentRiskAcceptanceRenewal', '10.0.12')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

update settings SET value = '102' where name = 'db_version';

COMMIT;
