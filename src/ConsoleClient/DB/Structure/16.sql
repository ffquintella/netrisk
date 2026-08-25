
-- Guarded so re-running this version converges: the column is already named `ImportSource`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.COLUMNS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'vulnerabilities' AND BINARY COLUMN_NAME = 'ImportSource') > 0,
                 'DO 0', 'ALTER TABLE `vulnerabilities` CHANGE COLUMN `ImportSorce` `ImportSource` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL AFTER `Details`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;