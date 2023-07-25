ALTER TABLE `files` MODIFY COLUMN `unique_name` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL AFTER `name`;
ADD UNIQUE INDEX `idx_unq_name`(`unique_name`) USING BTREE;