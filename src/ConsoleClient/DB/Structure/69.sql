START TRANSACTION;
-- Track 6 — Phase 3: relationships. Promote correlation columns to navigable, indexed FKs -> user(value).
-- Orphan-SAFE order: (1) widen columns to NULL, (2) LOG dangling refs to schema_upgrade_orphans,
-- (3) NULL the dangling refs, (4) best-effort backfill incidents.reported_by_id from the free-text
-- reported_by (kept for external reporters), (5) index the new FK columns, (6) ADD CONSTRAINT.
-- The ADD CONSTRAINT in step 6 would fail if step 3 hadn't run — cleanup is mandatory and ordered.
-- Down() (the EF migration) cannot un-null orphans; the schema_upgrade_orphans rows ARE the recovery record.

-- 0) Orphan-report artifact (write-only audit table; created once, retained across phases).
CREATE TABLE IF NOT EXISTS `schema_upgrade_orphans` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `phase` varchar(10) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `table_name` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `column_name` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `row_pk` int(11) NOT NULL,
    `dangling_value` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `captured_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`)
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 1) Widen the correlation columns to NULL FIRST so dangling refs can be NULLed and ON DELETE SET NULL works.
ALTER TABLE `risks` MODIFY COLUMN `submitted_by` int(11) NULL DEFAULT '1';
ALTER TABLE `risks` MODIFY COLUMN `owner` int(11) NULL;
ALTER TABLE `risks` MODIFY COLUMN `manager` int(11) NULL;
ALTER TABLE `framework_control_tests` MODIFY COLUMN `tester` int(11) NULL;
ALTER TABLE `incidents` ADD COLUMN `reported_by_id` int(11) NULL;

-- 2) Capture the orphan census BEFORE nulling. A "dangling" ref has no matching user.value (this also
--    catches the legacy 0 = "unassigned" sentinel, since no user has value 0).
INSERT INTO `schema_upgrade_orphans` (`phase`,`table_name`,`column_name`,`row_pk`,`dangling_value`,`captured_at`)
SELECT '3','risks','owner',r.`id`,CAST(r.`owner` AS CHAR),UTC_TIMESTAMP()
FROM `risks` r LEFT JOIN `user` u ON r.`owner` = u.`value`
WHERE r.`owner` IS NOT NULL AND u.`value` IS NULL;
INSERT INTO `schema_upgrade_orphans` (`phase`,`table_name`,`column_name`,`row_pk`,`dangling_value`,`captured_at`)
SELECT '3','risks','manager',r.`id`,CAST(r.`manager` AS CHAR),UTC_TIMESTAMP()
FROM `risks` r LEFT JOIN `user` u ON r.`manager` = u.`value`
WHERE r.`manager` IS NOT NULL AND u.`value` IS NULL;
INSERT INTO `schema_upgrade_orphans` (`phase`,`table_name`,`column_name`,`row_pk`,`dangling_value`,`captured_at`)
SELECT '3','risks','submitted_by',r.`id`,CAST(r.`submitted_by` AS CHAR),UTC_TIMESTAMP()
FROM `risks` r LEFT JOIN `user` u ON r.`submitted_by` = u.`value`
WHERE r.`submitted_by` IS NOT NULL AND u.`value` IS NULL;
INSERT INTO `schema_upgrade_orphans` (`phase`,`table_name`,`column_name`,`row_pk`,`dangling_value`,`captured_at`)
SELECT '3','framework_controls','control_owner',fc.`id`,CAST(fc.`control_owner` AS CHAR),UTC_TIMESTAMP()
FROM `framework_controls` fc LEFT JOIN `user` u ON fc.`control_owner` = u.`value`
WHERE fc.`control_owner` IS NOT NULL AND u.`value` IS NULL;
INSERT INTO `schema_upgrade_orphans` (`phase`,`table_name`,`column_name`,`row_pk`,`dangling_value`,`captured_at`)
SELECT '3','framework_control_tests','tester',fct.`id`,CAST(fct.`tester` AS CHAR),UTC_TIMESTAMP()
FROM `framework_control_tests` fct LEFT JOIN `user` u ON fct.`tester` = u.`value`
WHERE fct.`tester` IS NOT NULL AND u.`value` IS NULL;

-- 3) NULL the dangling refs (valid refs untouched).
UPDATE `risks` r LEFT JOIN `user` u ON r.`owner` = u.`value`
SET r.`owner` = NULL WHERE r.`owner` IS NOT NULL AND u.`value` IS NULL;
UPDATE `risks` r LEFT JOIN `user` u ON r.`manager` = u.`value`
SET r.`manager` = NULL WHERE r.`manager` IS NOT NULL AND u.`value` IS NULL;
UPDATE `risks` r LEFT JOIN `user` u ON r.`submitted_by` = u.`value`
SET r.`submitted_by` = NULL WHERE r.`submitted_by` IS NOT NULL AND u.`value` IS NULL;
UPDATE `framework_controls` fc LEFT JOIN `user` u ON fc.`control_owner` = u.`value`
SET fc.`control_owner` = NULL WHERE fc.`control_owner` IS NOT NULL AND u.`value` IS NULL;
UPDATE `framework_control_tests` fct LEFT JOIN `user` u ON fct.`tester` = u.`value`
SET fct.`tester` = NULL WHERE fct.`tester` IS NOT NULL AND u.`value` IS NULL;

-- 4) Best-effort, non-destructive backfill of incidents.reported_by_id from the free-text ReportedBy
--    (legacy PascalCase column, kept for external reporters) by EXACT, UNAMBIGUOUS user.name match.
--    Ambiguous / no-match rows stay NULL; the text is preserved.
UPDATE `incidents` i
JOIN `user` u ON u.`name` = i.`ReportedBy`
SET i.`reported_by_id` = u.`value`
WHERE i.`ReportedBy` IS NOT NULL AND i.`reported_by_id` IS NULL
  AND (SELECT COUNT(*) FROM `user` u2 WHERE u2.`name` = i.`ReportedBy`) = 1;

-- 5) Index the new FK columns (risks.owner/manager/submitted_by already have legacy indexes, reused below).
CREATE INDEX `IX_incidents_reported_by_id` ON `incidents` (`reported_by_id`);
CREATE INDEX `IX_framework_controls_control_owner` ON `framework_controls` (`control_owner`);
CREATE INDEX `IX_framework_control_tests_tester` ON `framework_control_tests` (`tester`);

-- 6) Add the FK constraints now that orphans are cleaned.
ALTER TABLE `framework_control_tests` ADD CONSTRAINT `fk_framework_control_tests_tester` FOREIGN KEY (`tester`) REFERENCES `user` (`value`) ON DELETE SET NULL;
ALTER TABLE `framework_controls` ADD CONSTRAINT `fk_framework_controls_control_owner` FOREIGN KEY (`control_owner`) REFERENCES `user` (`value`) ON DELETE SET NULL;
ALTER TABLE `incidents` ADD CONSTRAINT `fk_incidents_reported_by` FOREIGN KEY (`reported_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL;
ALTER TABLE `risks` ADD CONSTRAINT `fk_risks_manager` FOREIGN KEY (`manager`) REFERENCES `user` (`value`) ON DELETE SET NULL;
ALTER TABLE `risks` ADD CONSTRAINT `fk_risks_owner` FOREIGN KEY (`owner`) REFERENCES `user` (`value`) ON DELETE SET NULL;
ALTER TABLE `risks` ADD CONSTRAINT `fk_risks_submitted_by` FOREIGN KEY (`submitted_by`) REFERENCES `user` (`value`) ON DELETE SET NULL;

COMMIT;
