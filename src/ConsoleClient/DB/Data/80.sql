START TRANSACTION;

-- Track 8 (Risk Governance & Approval Workflows), governance core.
--
-- Pure DML inside a real transaction, which is what makes the version bump all-or-nothing: a
-- failure here rolls back whole and the retry starts from nothing applied. The DDL is in
-- Structure/80.sql, unguarded by a transaction and guarded statement by statement instead.

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260826163109_Track8GovernanceSchema', '10.0.11')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

-- 8.1.1 -- the finding-level acceptances Track 3 created predate start_date, so they take the
-- floor default the column was added with. Their start is the moment they were created; anything
-- else would be an invention.
UPDATE `risk_acceptances` SET `start_date` = `created_at`
WHERE `start_date` <= '1000-01-02 00:00:00';

-- 8.7.1 -- quantitative anchors on the ordinal scales.
--
-- The research behind this (Budescu et al.; Cox 2008) is that raters substitute their own meanings
-- for bare verbal labels, so two people rating "Likely" are not rating the same thing. These
-- definitions are the conventional CISO-survey anchors and are meant to be edited: they are a
-- starting point an organization tunes, not a claim about anybody's actual loss distribution.
--
-- Written with a WHERE guard on `definition IS NULL` so re-running the version does not overwrite
-- an organization's own wording -- the whole point of the column is that it gets edited.
UPDATE `likelihood` SET `definition` = 'Not expected in the foreseeable future: less than once every 20 years. No known occurrence at this organization or its peers.', `probability_min` = 0.0, `probability_max` = 0.05 WHERE `value` = 1 AND `definition` IS NULL;
UPDATE `likelihood` SET `definition` = 'Could happen but is not expected: roughly once every 5 to 20 years. Known in the sector, not here.', `probability_min` = 0.05, `probability_max` = 0.2 WHERE `value` = 2 AND `definition` IS NULL;
UPDATE `likelihood` SET `definition` = 'Plausible within a few years: roughly once every 2 to 5 years. Has happened here or to a close peer.', `probability_min` = 0.2, `probability_max` = 0.5 WHERE `value` = 3 AND `definition` IS NULL;
UPDATE `likelihood` SET `definition` = 'Expected within a year or two: roughly once every 1 to 2 years. Recurs unless something changes.', `probability_min` = 0.5, `probability_max` = 0.9 WHERE `value` = 4 AND `definition` IS NULL;
UPDATE `likelihood` SET `definition` = 'Expected at least annually, and often more than once. Treat as a matter of when, not whether.', `probability_min` = 0.9, `probability_max` = 1.0 WHERE `value` = 5 AND `definition` IS NULL;

UPDATE `impact` SET `definition` = 'Absorbed by normal operations. No regulatory, contractual or reputational consequence.', `impact_min` = 0, `impact_max` = 10000 WHERE `value` = 1 AND `definition` IS NULL;
UPDATE `impact` SET `definition` = 'Noticeable but contained: local disruption, handled by the owning team within days.', `impact_min` = 10000, `impact_max` = 100000 WHERE `value` = 2 AND `definition` IS NULL;
UPDATE `impact` SET `definition` = 'Material: a service degraded for customers, a reportable event, or a management-level response.', `impact_min` = 100000, `impact_max` = 1000000 WHERE `value` = 3 AND `definition` IS NULL;
UPDATE `impact` SET `definition` = 'Severe: sustained outage, regulatory action, or loss of a significant customer or contract.', `impact_min` = 1000000, `impact_max` = 10000000 WHERE `value` = 4 AND `definition` IS NULL;
UPDATE `impact` SET `definition` = 'Existential: threatens the organization''s licence to operate or its financial viability.', `impact_min` = 10000000, `impact_max` = 100000000 WHERE `value` = 5 AND `definition` IS NULL;

-- 8.2.2 -- the cadence setting comes back.
--
-- `next_review_date_uses` was seeded in version 1 and *deleted* in version 29 along with
-- `risk_appetite`, so the spec's claim that the setting "hints at the concept" describes a row that
-- has not existed for fifty versions. It is re-created here with the value that preserves today's
-- behaviour exactly; switching it to ResidualRisk is an explicit act.
INSERT INTO `settings` (`name`, `value`) VALUES ('next_review_date_uses', 'InherentRisk')
ON DUPLICATE KEY UPDATE `value` = `value`;

-- 8.3.2 -- the risk_workflow settings group.
--
-- Segregation of duties and the state machine are ON by default. That is a behaviour change on
-- upgrade, and it is the intended one: the finding these close is that convention was doing the work
-- of enforcement. Break-glass is off, and using it requires a reason that is written to the review.
INSERT INTO `settings` (`name`, `value`) VALUES ('risk_workflow_state_machine_enforced', 'true')
ON DUPLICATE KEY UPDATE `value` = `value`;
INSERT INTO `settings` (`name`, `value`) VALUES ('risk_workflow_segregation_of_duties', 'true')
ON DUPLICATE KEY UPDATE `value` = `value`;
INSERT INTO `settings` (`name`, `value`) VALUES ('risk_workflow_segregation_break_glass', 'false')
ON DUPLICATE KEY UPDATE `value` = `value`;
INSERT INTO `settings` (`name`, `value`) VALUES ('risk_workflow_residual_strategy', 'MitigationPercent')
ON DUPLICATE KEY UPDATE `value` = `value`;

-- 8.4.1 -- how long the field-level trail is kept. Five years covers a SOC 2 Type II look-back plus
-- margin; the cleanup job reads this and an operator can raise it.
INSERT INTO `settings` (`name`, `value`) VALUES ('audit_log_retention_days', '1825')
ON DUPLICATE KEY UPDATE `value` = `value`;

-- 8.7.2 -- the monetary thresholds a quantitatively scored risk maps into the existing RiskLevel
-- bands with, so lists, heatmaps and appetite rules keep working across both methods.
INSERT INTO `settings` (`name`, `value`) VALUES ('quantitative_band_thresholds', '10000,100000,1000000')
ON DUPLICATE KEY UPDATE `value` = `value`;
INSERT INTO `settings` (`name`, `value`) VALUES ('quantitative_iterations', '10000')
ON DUPLICATE KEY UPDATE `value` = `value`;

-- No global `risk_appetites` row is seeded, deliberately. An appetite is an organizational decision
-- with teeth -- above max_acceptable_residual an acceptance is refused outright -- and inventing one
-- during an upgrade would start blocking decisions an installation was making yesterday. With no row
-- the gate is inert and the admin screen says so; creating the first one is an explicit, audited act.

update settings SET value = '80' where name = 'db_version';

COMMIT;
