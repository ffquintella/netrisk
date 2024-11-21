START TRANSACTION;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241121143359_fixIncidentResponsePlan2', '8.0.10');

update settings SET value = '37' where name = 'db_version';

COMMIT;