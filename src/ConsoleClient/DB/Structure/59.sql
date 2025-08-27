START TRANSACTION;
ALTER TABLE `FaceIDUsers` ADD `FaceIdentification` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL;

ALTER TABLE `FaceIDUsers` ADD `LastUpdate` datetime(6) NOT NULL DEFAULT '0001-01-01 00:00:00';

ALTER TABLE `FaceIDUsers` ADD `LastUpdateUserId` int(11) NOT NULL DEFAULT 0;

CREATE TABLE `BiometricTransaction` (
                                        `Id` int NOT NULL AUTO_INCREMENT,
                                        `UserId` int(11) NOT NULL,
                                        `FaceIdUserId` int NOT NULL,
                                        `BiometricType` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
                                        `DateTime` datetime(6) NOT NULL,
                                        `TransactionObjectId` int NULL,
                                        `TransactionObjectType` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
                                        `TransactionDetails` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
                                        `TransactionResult` int NOT NULL,
                                        `BiometricLivenessAnchor` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
                                        CONSTRAINT `PRIMARY` PRIMARY KEY (`Id`),
                                        CONSTRAINT `fk_btrans_faceiduser` FOREIGN KEY (`FaceIdUserId`) REFERENCES `FaceIDUsers` (`Id`),
                                        CONSTRAINT `fk_btrans_user` FOREIGN KEY (`UserId`) REFERENCES `user` (`value`)
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX `IX_FaceIDUsers_LastUpdateUserId` ON `FaceIDUsers` (`LastUpdateUserId`);

CREATE UNIQUE INDEX `idx_biometic_anchor` ON `BiometricTransaction` (`BiometricLivenessAnchor`);

CREATE INDEX `IX_BiometricTransaction_FaceIdUserId` ON `BiometricTransaction` (`FaceIdUserId`);

CREATE INDEX `IX_BiometricTransaction_UserId` ON `BiometricTransaction` (`UserId`);

ALTER TABLE `FaceIDUsers` ADD CONSTRAINT `fk_faceid_last_update` FOREIGN KEY (`LastUpdateUserId`) REFERENCES `user` (`value`);

COMMIT;