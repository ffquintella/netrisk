START TRANSACTION;
-- Track 6 — Phase 4: indexing for performance + BLOB-as-text conversions.
-- 1) Drop the UNIQUE `id` index on framework_control_tests (redundant with the PRIMARY KEY).
--    NOTE: the EF migration also renames risk_scoring's `id1`->`id` index (a model-snapshot artifact from
--    two tables sharing the literal index name "id"); that rename is intentionally OMITTED here because the
--    real numbered-SQL schema already names risk_scoring's index `id` — renaming a non-existent `id1` would fail.
-- 2) Convert text-bearing BLOB columns to varchar/TEXT.
--    ENCODING NOTE: user.email is written by the app as UTF-8, so it converts directly to utf8mb4.
--    The legacy framework/permission BLOBs hold SimpleRisk seed text in Windows-1252/latin1 (they contain
--    bytes like 0x94 that are NOT valid UTF-8, and the app never writes them — see Track 6.3 analysis), so a
--    direct utf8mb4 MODIFY would error. They are converted via a latin1 round-trip: blob -> TEXT latin1
--    (every byte is valid latin1, lossless) -> TEXT utf8mb4 (proper cp1252->utf8mb4 transcoding). Validate on a
--    production clone first (a column with genuine UTF-8 rows would need a different path).
-- 3) Add hot-path indexes justified by ApplicationSieveProcessor (Vulnerability/Host) and the risk listing query.
ALTER TABLE `framework_control_tests` DROP INDEX `id`;

ALTER TABLE `user` MODIFY COLUMN `email` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL;

ALTER TABLE `permissions` MODIFY COLUMN `description` text CHARACTER SET latin1 NOT NULL;
ALTER TABLE `permissions` MODIFY COLUMN `description` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL;
ALTER TABLE `frameworks` MODIFY COLUMN `name` text CHARACTER SET latin1 NOT NULL;
ALTER TABLE `frameworks` MODIFY COLUMN `name` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL;
ALTER TABLE `frameworks` MODIFY COLUMN `description` text CHARACTER SET latin1 NOT NULL;
ALTER TABLE `frameworks` MODIFY COLUMN `description` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL;
ALTER TABLE `framework_controls` MODIFY COLUMN `supplemental_guidance` text CHARACTER SET latin1 NULL;
ALTER TABLE `framework_controls` MODIFY COLUMN `supplemental_guidance` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;
ALTER TABLE `framework_controls` MODIFY COLUMN `long_name` text CHARACTER SET latin1 NULL;
ALTER TABLE `framework_controls` MODIFY COLUMN `long_name` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;
ALTER TABLE `framework_controls` MODIFY COLUMN `description` text CHARACTER SET latin1 NULL;
ALTER TABLE `framework_controls` MODIFY COLUMN `description` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

CREATE INDEX `idx_vulnerabilities_first_detection` ON `vulnerabilities` (`FirstDetection`);
CREATE INDEX `idx_vulnerabilities_last_detection` ON `vulnerabilities` (`LastDetection`);
CREATE INDEX `idx_user_email` ON `user` (`email`);
CREATE INDEX `idx_risks_status_submission_date` ON `risks` (`status`, `submission_date`);
CREATE INDEX `idx_hosts_registration_date` ON `hosts` (`RegistrationDate`);
CREATE INDEX `idx_hosts_status` ON `hosts` (`Status`);

COMMIT;
