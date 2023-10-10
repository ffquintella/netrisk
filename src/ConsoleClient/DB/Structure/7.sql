drop table IF EXISTS audit_log;

CREATE TABLE `audit`  (
                           `Id` int NOT NULL AUTO_INCREMENT,
                           `UserId` int NOT NULL DEFAULT 0,
                           `Type` varchar(255) NOT NULL,
                           `TableName` varchar(255) NOT NULL,
                           `DateTime` datetime NOT NULL,
                           `OldValues` text NULL,
                           `NewValues` text NULL,
                           `AffectedColumns` varchar(255) NOT NULL,
                           `PrimaryKey` varchar(255) NULL,
                           PRIMARY KEY (`Id`),
                           INDEX `idx_audit_userid`(`UserId`) USING HASH,
                           INDEX `idx_audit_type`(`Type`) USING HASH,
                           INDEX `idx_audit_table`(`TableName`) USING HASH,
                           INDEX `idx_audit_date`(`DateTime`) USING HASH,
                           FULLTEXT INDEX `idx_audit_oldValues`(`OldValues`),
                           FULLTEXT INDEX `idx_audit_newVal`(`NewValues`),
                           INDEX `idx_audit_cols`(`AffectedColumns`) USING HASH,
                           INDEX `idx_audit_pk`(`PrimaryKey`) USING HASH
);