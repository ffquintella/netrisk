INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`) VALUES ('20241105192525_FirstMigration', '8.0.10');

update settings SET value = '30' where name = 'db_version';