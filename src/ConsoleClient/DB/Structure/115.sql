-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so this script carries no
-- transaction; every statement is guarded instead, and applying the version again converges on the
-- same schema.

-- M58, T281-T282 (S39 §3.4, §8.2, D-14, D-18, D-20, D-21) -- asset data the risk index read with a workaround.
-- EF migration 20261118164710_HostAssessmentAndEntityAttribution.

-- hosts: when a scan last assessed the host (UTC; written only by scanner and code-scanner ingestion, D-18), and who
-- wrote entity_id (manual / cmdb / rule / visionone / import; NULL = unknown or unassigned, D-20).
ALTER TABLE `hosts` ADD COLUMN IF NOT EXISTS `last_assessed_at` datetime NULL;
ALTER TABLE `hosts` ADD COLUMN IF NOT EXISTS `entity_source` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

-- The repository or project a code-scanner import named, on the import and on its findings (D-21); part of the dedup key.
ALTER TABLE `scan_imports` ADD COLUMN IF NOT EXISTS `repository` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;
ALTER TABLE `vulnerabilities` ADD COLUMN IF NOT EXISTS `repository` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

-- Per-connection entity re-stamp policy (DAL.Enums.RestampPolicy: 1 OnlyUnassigned, the default; 2 FollowMapping).
ALTER TABLE `trendmicro_connections` ADD COLUMN IF NOT EXISTS `entity_restamp_policy` int(11) NOT NULL DEFAULT 1;
ALTER TABLE `issue_tracker_connections` ADD COLUMN IF NOT EXISTS `entity_restamp_policy` int(11) NOT NULL DEFAULT 1;
