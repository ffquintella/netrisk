START TRANSACTION;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260825201541_Track4IntegrationsSchema', '10.0.11')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

-- Seed the four notification event digest defaults? No: a subscription nobody asked for is a
-- notification nobody expects. Track 4 ships with an empty subscription matrix and the admin UI
-- creates the first row, which is also what keeps an upgrade from suddenly emailing an entire
-- organization.

update settings SET value = '79' where name = 'db_version';

COMMIT;
