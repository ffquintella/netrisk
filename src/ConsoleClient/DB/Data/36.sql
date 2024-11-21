START TRANSACTION;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241121115334_fixIncidentResponsePlan', '8.0.10');

update settings SET value = '36' where name = 'db_version';

COMMIT;
