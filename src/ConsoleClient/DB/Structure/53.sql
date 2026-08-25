-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema — that, and not
-- a transaction, is what makes the upgrade safe to retry.

ALTER TABLE `Incidents` ADD COLUMN IF NOT EXISTS `Category` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT '';

ALTER TABLE `Incidents` ADD COLUMN IF NOT EXISTS `ImpactedEntityId` int(11) NULL;

CREATE INDEX IF NOT EXISTS `idx_category` ON `Incidents` (`Category`);

CREATE INDEX IF NOT EXISTS `IX_Incidents_ImpactedEntityId` ON `Incidents` (`ImpactedEntityId`);

ALTER TABLE `Incidents` ADD CONSTRAINT `fk_inc_impacted_entity` FOREIGN KEY IF NOT EXISTS (`ImpactedEntityId`) REFERENCES `entities` (`Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241229213258_NewIncidentFields2', '9.0.0')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

