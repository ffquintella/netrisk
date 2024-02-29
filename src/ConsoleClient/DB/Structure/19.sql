ALTER TABLE `reports`
    ADD COLUMN `status` int ZEROFILL NOT NULL DEFAULT 0 AFTER `type`;

ALTER TABLE `reports` DROP FOREIGN KEY `fk_file_id`;

ALTER TABLE `reports`
    MODIFY COLUMN `fileId` int NULL AFTER `creatorId`,
    ADD CONSTRAINT `fk_file_id` FOREIGN KEY (`fileId`) REFERENCES `nr_files` (`id`) ON DELETE CASCADE;

ALTER TABLE `reports`
    DROP INDEX `idx_name`,
    ADD INDEX `idx_name`(`name` ASC) USING BTREE;