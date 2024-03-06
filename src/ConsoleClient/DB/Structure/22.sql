ALTER TABLE `audit`
    MODIFY COLUMN `AffectedColumns` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL AFTER `NewValues`;