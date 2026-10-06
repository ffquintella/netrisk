-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema -- that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- Track 9, Stage 9.2 (S42, M40) -- structured scenario, record discrimination and evidence confidence.
--
--   risks           Four free-text scenario fields (cause/threat, vulnerability/condition, central
--                   event, consequences) and evidence_confidence (1 Confirmed, 2 Indicative,
--                   3 Hypothesis). All five NULL on every existing risk: the stage does not back-fill
--                   them from Subject/Assessment/Notes (S42 §3), and NULL confidence is "not declared".
--   pending_risks   The hypothesis record can now be registered standalone: assessment_id and
--                   assessment_answer_id become NULLable, origin (1 Assessment, 2 Standalone; every
--                   existing row is 1) and submitted_by_id (the standalone author) are added. And it
--                   becomes entity-scoped: entity_id, read by the same query filter and write guard as
--                   risks.entity_id, back-filled in Data/89.sql from the raising assessment's entity.
--   incidents       kind (1 Incident, 2 NearMiss); every existing row is an incident.
--
-- Columns only, nothing dropped or renamed: no destructive gate, no observation window, no
-- information_schema probe. The two MODIFY COLUMN statements restate the target definition, so a
-- second application is a no-op.
--
-- Version 89 rather than the 89/90 S40 §7.6 had reserved for M52/M53 and M56/M57: this stage merged
-- first, and the reservation shifts to 90/91 by amendment (S42 §5, R1; S40 §7.6).

ALTER TABLE `risks` ADD COLUMN IF NOT EXISTS `scenario_cause` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `risks` ADD COLUMN IF NOT EXISTS `scenario_vulnerability` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `risks` ADD COLUMN IF NOT EXISTS `scenario_central_event` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `risks` ADD COLUMN IF NOT EXISTS `scenario_consequences` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `risks` ADD COLUMN IF NOT EXISTS `evidence_confidence` int(11) NULL;

ALTER TABLE `pending_risks` MODIFY COLUMN `assessment_id` int(11) NULL;

ALTER TABLE `pending_risks` MODIFY COLUMN `assessment_answer_id` int(11) NULL;

ALTER TABLE `pending_risks` ADD COLUMN IF NOT EXISTS `origin` int(11) NOT NULL DEFAULT 1;

ALTER TABLE `pending_risks` ADD COLUMN IF NOT EXISTS `submitted_by_id` int(11) NULL;

CREATE INDEX IF NOT EXISTS `idx_pending_risks_submitted_by_id` ON `pending_risks` (`submitted_by_id`);

ALTER TABLE `pending_risks` ADD CONSTRAINT `fk_pending_risks_submitted_by_id` FOREIGN KEY IF NOT EXISTS (`submitted_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL;

ALTER TABLE `pending_risks` ADD COLUMN IF NOT EXISTS `entity_id` int(11) NULL;

CREATE INDEX IF NOT EXISTS `idx_pending_risks_entity_id` ON `pending_risks` (`entity_id`);

ALTER TABLE `pending_risks` ADD CONSTRAINT `fk_pending_risks_entity_id` FOREIGN KEY IF NOT EXISTS (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE SET NULL;

ALTER TABLE `incidents` ADD COLUMN IF NOT EXISTS `kind` int(11) NOT NULL DEFAULT 1;
