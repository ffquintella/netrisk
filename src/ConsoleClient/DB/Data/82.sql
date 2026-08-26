START TRANSACTION;

-- The security schema Track 7 deferred to Track 8 (NR-2026-017, NR-2026-028, NR-2026-008b).

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260826163200_Track7DeferredSecuritySchema', '10.0.11')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

-- NR-2026-017 -- backfill the attachment's entity from whichever parent it hangs off. Only the
-- parents that carry an entity_id of their own can contribute one; a file attached to something
-- unscoped keeps a NULL, which the query filter treats as visible. That is the honest outcome: the
-- fix closes cross-tenant reads for every attachment whose owner is known, and does not pretend to
-- have classified the ones whose owner is not.
UPDATE `nr_files` f
    JOIN `risks` r ON r.`id` = f.`risk_id`
SET f.`entity_id` = r.`entity_id`
WHERE f.`entity_id` IS NULL AND r.`entity_id` IS NOT NULL;

UPDATE `nr_files` f
    JOIN `mitigations` m ON m.`id` = f.`mitigation_id`
    JOIN `risks` r ON r.`id` = m.`risk_id`
SET f.`entity_id` = r.`entity_id`
WHERE f.`entity_id` IS NULL AND r.`entity_id` IS NOT NULL;

UPDATE `nr_files` f
    JOIN `incidents` i ON i.`id` = f.`IncidentId`
SET f.`entity_id` = i.`entity_id`
WHERE f.`entity_id` IS NULL AND i.`entity_id` IS NOT NULL;

UPDATE `nr_files` f
    JOIN `risk_acceptances` ra ON ra.`id` = f.`risk_acceptance_id`
SET f.`entity_id` = ra.`entity_id`
WHERE f.`entity_id` IS NULL AND ra.`entity_id` IS NOT NULL;

-- NR-2026-008b -- how the persisted counter behaves. The values match what LoginAttemptTracker
-- enforced in memory, so the fix changes where the counter lives and not how strict it is.
INSERT INTO `settings` (`name`, `value`) VALUES ('login_lockout_max_failures', '5')
ON DUPLICATE KEY UPDATE `value` = `value`;
INSERT INTO `settings` (`name`, `value`) VALUES ('login_lockout_window_minutes', '15')
ON DUPLICATE KEY UPDATE `value` = `value`;
INSERT INTO `settings` (`name`, `value`) VALUES ('login_lockout_duration_minutes', '15')
ON DUPLICATE KEY UPDATE `value` = `value`;

update settings SET value = '82' where name = 'db_version';

COMMIT;
