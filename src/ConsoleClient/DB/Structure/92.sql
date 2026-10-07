-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema -- that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- GitHub #80 (T297, S44) -- a comment and evidence files on each answer of an assessment run.
--
--   assessment_run_answers.comment     TEXT NULL. The assessor's note on one answer; the 4 000-character
--                                      bound is the API's. NULL on every existing row: nobody wrote one.
--   nr_files.assessment_run_answer_id  int NULL, FK to assessment_run_answers(id) ON DELETE CASCADE --
--                                      the one-nullable-FK-per-attachment-target pattern of
--                                      risk_acceptance_id. Deleting an answer, or the run that cascades
--                                      to its answers, takes its evidence with it.
--
-- Version 92 rather than the 92/93 S40 section 7.6 had reserved for M52 and M56/M57: this change merged
-- first, so S40 shifts its scripts to 93 and 94 (the same rule #79 and Stages 9.1-9.3 followed).

ALTER TABLE `assessment_run_answers` ADD COLUMN IF NOT EXISTS `comment` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `nr_files` ADD COLUMN IF NOT EXISTS `assessment_run_answer_id` int(11) NULL;

CREATE INDEX IF NOT EXISTS `idx_nr_files_assessment_run_answer_id` ON `nr_files` (`assessment_run_answer_id`);

ALTER TABLE `nr_files` ADD CONSTRAINT `fk_nr_files_assessment_run_answer_id` FOREIGN KEY IF NOT EXISTS (`assessment_run_answer_id`) REFERENCES `assessment_run_answers` (`id`) ON DELETE CASCADE;
