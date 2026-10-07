START TRANSACTION;

-- GitHub #80 (T297, S44) -- a comment and evidence files on each answer of an assessment run.
--
-- Pure DML. A single CREATE or ALTER here would implicitly commit the transaction out from under the
-- rest of the script, and the db_version bump below would stop being the commit point that makes a
-- failed Data script roll back whole.
--
-- Nothing is back-filled: no existing answer has a comment or evidence.

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261007125529_AssessmentAnswerEvidence', '10.0.12')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

update settings SET value = '92' where name = 'db_version';

COMMIT;
