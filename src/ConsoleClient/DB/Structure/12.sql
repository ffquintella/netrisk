drop table IF EXISTS assessment_answers_to_asset_groups;
drop table IF EXISTS assessment_scoring;
drop table IF EXISTS assessment_scoring_contributing_impacts;
drop table IF EXISTS assessment_answers_to_assets;


CREATE TABLE `assessment_runs`  (
                                    `Id` int NOT NULL AUTO_INCREMENT,
                                    `AssessmentId` int NOT NULL,
                                    `EntityId` int NOT NULL,
                                    `RunDate` datetime NULL,
                                    `AnalystId` int NULL,
                                    PRIMARY KEY (`Id`),
                                    CONSTRAINT `fkAssessment` FOREIGN KEY (`Id`) REFERENCES `assessments` (`id`) ON DELETE CASCADE,
                                    CONSTRAINT `fkEntity` FOREIGN KEY (`EntityId`) REFERENCES `entities` (`Id`) ON DELETE CASCADE,
                                    CONSTRAINT `fkAnalystId` FOREIGN KEY (`AnalystId`) REFERENCES `user` (`value`) ON DELETE SET NULL
);

CREATE TABLE `assessment_runs_answers`  (
                                       `Id` int NOT NULL AUTO_INCREMENT,
                                       `AnswerId` int NOT NULL,
                                       `Value` varchar(255) NULL,
                                       `RunId` int NOT NULL,
                                       PRIMARY KEY (`Id`),
                                       CONSTRAINT `fkAnswerId` FOREIGN KEY (`AnswerId`) REFERENCES `assessment_answers` (`id`) ON DELETE CASCADE,
                                       CONSTRAINT `fkRunId` FOREIGN KEY (`RunId`) REFERENCES `assessment_runs` (`id`) ON DELETE CASCADE
);