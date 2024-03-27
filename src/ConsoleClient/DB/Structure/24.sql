

CREATE TABLE `messages`  (
                                       `Id` int NOT NULL AUTO_INCREMENT,
                                       `UserId` int NOT NULL,
                                       `CreatedAt` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                                       `ReceivedAt` datetime NULL,
                                       `Message` text NULL,
                                       `Status` int NULL DEFAULT 1,
                                       PRIMARY KEY (`Id`),
                                       INDEX `idx_created_at`(`CreatedAt`) USING BTREE,
                                       INDEX `idx_received_at`(`ReceivedAt`) USING BTREE,
                                       INDEX `idx_status`(`Status`) USING BTREE,
                                       CONSTRAINT `fK_user_message` FOREIGN KEY (`UserId`) REFERENCES `user` (`value`) ON DELETE CASCADE ON UPDATE CASCADE
);

CREATE TABLE `jobs`  (
                                       `Id` int NOT NULL AUTO_INCREMENT,
                                       `Status` int NOT NULL DEFAULT 1,
                                       `StartedAt` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
                                       `LastUpdate` datetime NULL,
                                       `OwnerId` int NULL,
                                       `Result` blob NULL,
                                       `Parameters` blob NULL,
                                       `Title` varchar(255) NULL,
                                       `CancellationToken` blob NULL,
                                       `Progress` int NOT NULL DEFAULT 0,
                                       PRIMARY KEY (`Id`),
                                       INDEX `idx_started`(`StartedAt`) USING BTREE,
                                       INDEX `idx_updated`(`LastUpdate`) USING BTREE,
                                       INDEX `idx_status`(`Status`) USING BTREE,
                                       CONSTRAINT `fk_user_job` FOREIGN KEY (`OwnerId`) REFERENCES `user` (`value`) ON DELETE CASCADE ON UPDATE CASCADE
);

