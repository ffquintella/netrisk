START TRANSACTION;
ALTER TABLE `BiometricTransaction` MODIFY COLUMN `ValidationObjectData` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

COMMIT;
