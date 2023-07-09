ALTER TABLE `addons_client_registration`
    ADD INDEX (`ExternalId`) USING BTREE;

ALTER TABLE `addons_client_registration`
    MODIFY COLUMN `Id` int(11) NOT NULL AUTO_INCREMENT FIRST;