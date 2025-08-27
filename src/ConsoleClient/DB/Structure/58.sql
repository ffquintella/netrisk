START TRANSACTION;
ALTER TABLE `Incidents` MODIFY COLUMN `ReportedByEntity` tinyint(1) NULL DEFAULT 0;

CREATE TABLE `FaceIDUsers` (
                               `Id` int NOT NULL AUTO_INCREMENT,
                               `UserId` int(11) NOT NULL,
                               `IsEnabled` tinyint(1) NOT NULL,
                               `SignatureSeed` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
                               CONSTRAINT `PRIMARY` PRIMARY KEY (`Id`),
                               CONSTRAINT `FK_FaceIDUsers_user_UserId` FOREIGN KEY (`UserId`) REFERENCES `user` (`value`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE UNIQUE INDEX `idx_signature_seed` ON `FaceIDUsers` (`SignatureSeed`);

CREATE UNIQUE INDEX `IX_FaceIDUsers_UserId` ON `FaceIDUsers` (`UserId`);

COMMIT;