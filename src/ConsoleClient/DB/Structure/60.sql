START TRANSACTION;
ALTER TABLE `BiometricTransaction` RENAME COLUMN `DateTime` TO `StartTime`;

ALTER TABLE `BiometricTransaction` MODIFY COLUMN `BiometricLivenessAnchor` varchar(1000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL;

ALTER TABLE `BiometricTransaction` ADD `ResultTime` datetime(6) NOT NULL DEFAULT '0001-01-01 00:00:00';

ALTER TABLE `BiometricTransaction` ADD `TransactionId` char(36) COLLATE ascii_general_ci NULL;

ALTER TABLE `BiometricTransaction` ADD `TransactionResultDetails` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `BiometricTransaction` ADD `ValidationObjectData` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `BiometricTransaction` ADD `ValidationSequence` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL;

COMMIT;