SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

-- ----------------------------
-- Table structure for hosts
-- ----------------------------
DROP TABLE IF EXISTS `hosts`;
CREATE TABLE `hosts`  (
                          `Id` int(11) NOT NULL AUTO_INCREMENT,
                          `Ip` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL,
                          `HostName` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL,
                          `Status` smallint(6) NOT NULL DEFAULT 1,
                          `Source` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'manual',
                          `RegistrationDate` datetime NOT NULL DEFAULT current_timestamp(),
                          `LastVerificationDate` datetime NULL DEFAULT NULL,
                          `TeamId` int(11) NULL DEFAULT NULL,
                          `Comment` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL,
                          PRIMARY KEY (`Id`) USING BTREE,
                          INDEX `fk_host_team`(`TeamId`) USING BTREE,
                          CONSTRAINT `fk_host_team` FOREIGN KEY (`TeamId`) REFERENCES `team` (`value`) ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_unicode_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for vulnerabilities
-- ----------------------------
DROP TABLE IF EXISTS `vulnerabilities`;
CREATE TABLE `vulnerabilities`  (
                                    `Id` int(11) NOT NULL AUTO_INCREMENT,
                                    `Score` double NULL DEFAULT NULL,
                                    `Severity` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL,
                                    `FirstDetection` datetime NOT NULL DEFAULT current_timestamp(),
                                    `DetectionCount` int(11) NOT NULL DEFAULT 1,
                                    `LastDetection` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE CURRENT_TIMESTAMP,
                                    `Description` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL,
                                    `Solution` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL,
                                    `Title` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
                                    `Comments` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL,
                                    `Status` smallint(5) UNSIGNED NOT NULL DEFAULT 1,
                                    `HostId` int(11) NULL DEFAULT NULL,
                                    `AnalystId` int(11) NULL DEFAULT NULL,
                                    `FixTeamId` int(11) NULL DEFAULT NULL,
                                    `Technology` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL,
                                    PRIMARY KEY (`Id`) USING BTREE,
                                    INDEX `idx_status`(`Status`) USING BTREE,
                                    INDEX `idx_technology`(`Technology`) USING BTREE,
                                    INDEX `fk_vulnerability_host`(`HostId`) USING BTREE,
                                    INDEX `fk_vulnerarbility_user`(`AnalystId`) USING BTREE,
                                    INDEX `fk_vulnerability_team`(`FixTeamId`) USING BTREE,
                                    FULLTEXT INDEX `idx_title`(`Title`),
                                    CONSTRAINT `fk_vulnerability_host` FOREIGN KEY (`HostId`) REFERENCES `hosts` (`Id`) ON DELETE SET NULL ON UPDATE CASCADE,
                                    CONSTRAINT `fk_vulnerability_team` FOREIGN KEY (`FixTeamId`) REFERENCES `team` (`value`) ON DELETE SET NULL ON UPDATE CASCADE,
                                    CONSTRAINT `fk_vulnerarbility_user` FOREIGN KEY (`AnalystId`) REFERENCES `user` (`value`) ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_unicode_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for risks_to_vulnerabilities
-- ----------------------------
DROP TABLE IF EXISTS `risks_to_vulnerabilities`;
CREATE TABLE `risks_to_vulnerabilities`  (
                                             `risk_id` int(11) NOT NULL,
                                             `vulnerability_id` int(11) NOT NULL,
                                             PRIMARY KEY (`risk_id`, `vulnerability_id`) USING BTREE,
                                             INDEX `fk_rv_v`(`vulnerability_id`) USING BTREE,
                                             CONSTRAINT `fk_rv_r` FOREIGN KEY (`risk_id`) REFERENCES `risks` (`id`) ON DELETE CASCADE ON UPDATE CASCADE,
                                             CONSTRAINT `fk_rv_v` FOREIGN KEY (`vulnerability_id`) REFERENCES `vulnerabilities` (`Id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_unicode_ci ROW_FORMAT = Dynamic;

RENAME TABLE files TO nr_files;


SET FOREIGN_KEY_CHECKS = 1;