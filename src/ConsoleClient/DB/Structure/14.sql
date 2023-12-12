
ALTER TABLE `assessment_runs`
    ADD COLUMN `Status` int NOT NULL AFTER `AnalystId`,
    ADD INDEX `idxStatus`(`Status`) USING BTREE;

