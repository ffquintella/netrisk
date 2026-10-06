START TRANSACTION;

-- Track 9, Stage 9.2 (S42, M40) -- structured scenario, record discrimination and evidence confidence.
--
-- Pure DML. A single CREATE or ALTER here would implicitly commit the transaction out from under the
-- rest of the script, and the db_version bump below would stop being the commit point that makes a
-- failed Data script roll back whole.
--
-- No scenario text is copied or back-filled: the four scenario fields and the evidence confidence stay
-- NULL on every existing risk (guessing a decomposition of text nobody wrote with that intent would
-- produce wrong scenarios that look right -- S42 §3), and the two new NOT NULL columns take their
-- DEFAULT 1 (pending_risks.origin = Assessment, incidents.kind = Incident), which is what every
-- existing row is.

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261006182711_Track9StructuredScenario', '10.0.12')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

-- The one back-fill: an assessment-raised pending risk belongs to the entity its assessment belongs to
-- (assessments.entity_id, scoped since 74). Without it every existing row would read as
-- organization-wide and vanish from every scoped triager the moment pending_risks gains its query
-- filter. Rows whose assessment has no entity stay NULL (organization-wide). The IS NULL predicate
-- makes a retry a no-op.
UPDATE `pending_risks` p
JOIN `assessments` a ON a.`id` = p.`assessment_id`
SET p.`entity_id` = a.`entity_id`
WHERE p.`entity_id` IS NULL AND a.`entity_id` IS NOT NULL;

update settings SET value = '89' where name = 'db_version';

COMMIT;
