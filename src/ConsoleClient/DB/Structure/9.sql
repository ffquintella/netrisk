
CREATE TABLE IF NOT EXISTS `hosts_services`  (
                                       `Id` int NOT NULL,
                                       `HostId` int NOT NULL,
                                       `Name` varchar(255) NOT NULL,
                                       `Protocol` varchar(255) NOT NULL,
                                       `Port` int NULL,
                                       PRIMARY KEY (`Id`),
                                       INDEX `idx_name`(`Name`) USING BTREE,
                                       INDEX `idx_port`(`Port`) USING BTREE,
                                       INDEX `idx_protocol`(`Protocol`) USING BTREE,
                                       CONSTRAINT `fk_host` FOREIGN KEY (`HostId`) REFERENCES `hosts` (`Id`) ON DELETE CASCADE
);


ALTER TABLE `vulnerabilities` DROP FOREIGN KEY `fk_vulnerability_host`;

ALTER TABLE `vulnerabilities`
    ADD COLUMN `Details` text NULL AFTER `Technology`,
    ADD COLUMN `ImportSorce` varchar(255) NULL AFTER `Details`,
    ADD COLUMN `ImportHash` varchar(255) NULL AFTER `ImportSorce`,
    ADD COLUMN `HostServiceId` int NULL AFTER `ImportHash`,
    ADD CONSTRAINT `fk_vulnerability_host` FOREIGN KEY (`HostId`) REFERENCES `hosts` (`Id`) ON DELETE CASCADE ON UPDATE CASCADE,
    ADD CONSTRAINT `fk_hosts_service` FOREIGN KEY (`HostServiceId`) REFERENCES `hosts_services` (`Id`) ON DELETE CASCADE ON UPDATE CASCADE;

ALTER TABLE `hosts`
    ADD COLUMN `OS` varchar(255) NULL AFTER `Comment`,
    ADD COLUMN `FQDN` varchar(255) NULL AFTER `OS`,
    ADD COLUMN `MacAddress` varchar(255) NULL AFTER `FQDN`;