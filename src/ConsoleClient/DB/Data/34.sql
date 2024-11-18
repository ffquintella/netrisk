START TRANSACTION;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241117151222_IncidentResponsePlan', '8.0.10');

INSERT INTO permissions (`key`, `name`, `description`,  `order`)
VALUES ('incident-response-plans', 'Incident Response Plans',  RPAD('', 65535, CHAR(7)), 16);

update settings SET value = '34' where name = 'db_version';

COMMIT;