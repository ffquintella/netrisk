
ALTER TABLE `assessment_runs`
    ADD COLUMN `Status` int NOT NULL AFTER `AnalystId`,
    ADD INDEX `idxStatus`(`Status`) USING BTREE;

ALTER TABLE `assessment_questions` DROP FOREIGN KEY `fk_assessment`;