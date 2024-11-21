START TRANSACTION;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `HasBeenTested` tinyint(1) NOT NULL DEFAULT 0;

update settings SET value = '35' where name = 'db_version';

COMMIT;
