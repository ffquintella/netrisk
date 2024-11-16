INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241105195812_RiskToRiskCatalog', '8.0.10');

update settings SET value = '32' where name = 'db_version';