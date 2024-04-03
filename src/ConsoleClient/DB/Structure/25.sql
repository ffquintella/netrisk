ALTER TABLE `messages`
    ADD COLUMN `Type` int NOT NULL DEFAULT 1 AFTER `ChatId`,
ADD INDEX `idx_msg_type`(`Type`) USING BTREE;