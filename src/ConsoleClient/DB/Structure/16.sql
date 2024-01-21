
ALTER TABLE `vulnerabilities`
    CHANGE COLUMN `ImportSorce` `ImportSource` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL AFTER `Details`;