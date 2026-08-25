-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema — that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- Guarded so re-running this version converges: the column is already named `StartTime`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.COLUMNS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'BiometricTransaction' AND BINARY COLUMN_NAME = 'StartTime') > 0,
                 'DO 0', 'ALTER TABLE `BiometricTransaction` RENAME COLUMN `DateTime` TO `StartTime`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

ALTER TABLE `BiometricTransaction` MODIFY COLUMN `BiometricLivenessAnchor` varchar(1000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL;

ALTER TABLE `BiometricTransaction` ADD COLUMN IF NOT EXISTS `ResultTime` datetime(6) NOT NULL DEFAULT '0001-01-01 00:00:00';

ALTER TABLE `BiometricTransaction` ADD COLUMN IF NOT EXISTS `TransactionId` char(36) COLLATE ascii_general_ci NULL;

ALTER TABLE `BiometricTransaction` ADD COLUMN IF NOT EXISTS `TransactionResultDetails` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `BiometricTransaction` ADD COLUMN IF NOT EXISTS `ValidationObjectData` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `BiometricTransaction` ADD COLUMN IF NOT EXISTS `ValidationSequence` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL;

