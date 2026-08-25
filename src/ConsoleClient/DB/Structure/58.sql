-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema — that, and not
-- a transaction, is what makes the upgrade safe to retry.

ALTER TABLE `Incidents` MODIFY COLUMN `ReportedByEntity` tinyint(1) NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS `FaceIDUsers` (
                               `Id` int NOT NULL AUTO_INCREMENT,
                               `UserId` int(11) NOT NULL,
                               `IsEnabled` tinyint(1) NOT NULL,
                               `SignatureSeed` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
                               CONSTRAINT `PRIMARY` PRIMARY KEY (`Id`),
                               CONSTRAINT `FK_FaceIDUsers_user_UserId` FOREIGN KEY (`UserId`) REFERENCES `user` (`value`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE UNIQUE INDEX IF NOT EXISTS `idx_signature_seed` ON `FaceIDUsers` (`SignatureSeed`);

CREATE UNIQUE INDEX IF NOT EXISTS `IX_FaceIDUsers_UserId` ON `FaceIDUsers` (`UserId`);

