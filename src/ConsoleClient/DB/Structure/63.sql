START TRANSACTION;
CREATE TABLE `schema_upgrade_log` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `phase` varchar(10) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `start_version` int(11) NOT NULL,
    `target_version` int(11) NOT NULL,
    `status` varchar(20) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `environment` varchar(20) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `operator` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `backup_path` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `backup_hash` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `notes` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `applied_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`)
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX `idx_schema_upgrade_log_phase_status` ON `schema_upgrade_log` (`phase`, `status`);

COMMIT;
