CREATE TABLE IF NOT EXISTS `reports`  (
                                       `id` int NOT NULL AUTO_INCREMENT,
                                       `name` varchar(255) NOT NULL,
                                       `creatorId` int NOT NULL,
                                       `fileId` int NOT NULL,
                                       `parameters` text NULL,
                                       `creationDate` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
                                       `type` int NOT NULL DEFAULT 1,
                                       PRIMARY KEY (`id`),
                                       UNIQUE INDEX `idx_name`(`name`) USING HASH,
                                       CONSTRAINT `fk_creator_id` FOREIGN KEY (`creatorId`) REFERENCES `user` (`value`),
                                       CONSTRAINT `fk_file_id` FOREIGN KEY (`fileId`) REFERENCES `nr_files` (`id`) ON DELETE CASCADE 
);