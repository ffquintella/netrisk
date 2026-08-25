
ALTER TABLE `assessment_runs`
    ADD COLUMN IF NOT EXISTS `Status` int NOT NULL AFTER `AnalystId`,
    ADD INDEX IF NOT EXISTS `idxStatus`(`Status`) USING BTREE;

