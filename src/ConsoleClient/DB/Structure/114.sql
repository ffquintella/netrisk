-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so this script carries no
-- transaction; every statement is guarded instead, and applying the version again converges on the
-- same schema.

-- M58, T279-T280 (S39 §14, D-15, D-16) -- incident and assessment data the risk index read with a workaround.
-- EF migration 20261118162742_IncidentSeverityAndAssessmentSubmission.

-- incidents: the severity that replaces the category anchor (int, Model enum IncidentSeverity: 1 Low .. 4 Critical;
-- NULL = not assessed) and the UTC resolution date, stamped by the server when the status enters the closed set (D-15).
ALTER TABLE `incidents` ADD COLUMN IF NOT EXISTS `severity` int(11) NULL;
ALTER TABLE `incidents` ADD COLUMN IF NOT EXISTS `resolved_at` datetime NULL;

-- assessment_runs: when the run was submitted (UTC, never moved by a later save) and the score the server recorded then (D-16).
ALTER TABLE `assessment_runs` ADD COLUMN IF NOT EXISTS `submitted_at` datetime NULL;
ALTER TABLE `assessment_runs` ADD COLUMN IF NOT EXISTS `score` double NULL;

-- assessment_run_answers: the option an answer chose, by id instead of by its text (D-16); SET NULL when the option goes.
ALTER TABLE `assessment_run_answers` ADD COLUMN IF NOT EXISTS `assessment_answer_id` int(11) NULL;

CREATE INDEX IF NOT EXISTS `idx_assessment_run_answers_assessment_answer_id` ON `assessment_run_answers` (`assessment_answer_id`);

ALTER TABLE `assessment_run_answers` ADD CONSTRAINT `fk_assessment_run_answers_assessment_answer_id` FOREIGN KEY IF NOT EXISTS (`assessment_answer_id`) REFERENCES `assessment_answers` (`id`) ON DELETE SET NULL;
