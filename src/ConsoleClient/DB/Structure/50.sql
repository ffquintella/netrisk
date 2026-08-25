-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema — that, and not
-- a transaction, is what makes the upgrade safe to retry.

ALTER TABLE `Incidents` ADD COLUMN IF NOT EXISTS `AssignedToId` int(11) NULL;

ALTER TABLE `Incidents` ADD COLUMN IF NOT EXISTS `Recomendations` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL;

ALTER TABLE `Incidents` ADD COLUMN IF NOT EXISTS `ReportDate` datetime NOT NULL DEFAULT current_timestamp();

ALTER TABLE `Incidents` ADD COLUMN IF NOT EXISTS `ReportEntityId` int(11) NULL;

ALTER TABLE `Incidents` ADD COLUMN IF NOT EXISTS `ReportedBy` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL;

ALTER TABLE `Incidents` ADD COLUMN IF NOT EXISTS `ReportedByEntity` tinyint(1) NOT NULL DEFAULT 0;

CREATE FULLTEXT INDEX IF NOT EXISTS `idx_reported_by` ON `Incidents` (`ReportedBy`);

CREATE INDEX IF NOT EXISTS `IX_Incidents_AssignedToId` ON `Incidents` (`AssignedToId`);

CREATE INDEX IF NOT EXISTS `IX_Incidents_ReportEntityId` ON `Incidents` (`ReportEntityId`);

ALTER TABLE `Incidents` ADD CONSTRAINT `fk_inc_report_entity` FOREIGN KEY IF NOT EXISTS (`ReportEntityId`) REFERENCES `entities` (`Id`);

ALTER TABLE `Incidents` ADD CONSTRAINT `fk_inc_report_user` FOREIGN KEY IF NOT EXISTS (`AssignedToId`) REFERENCES `user` (`value`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241226143834_NewIncidentFields', '9.0.0')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

