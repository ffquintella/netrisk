-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema -- that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- Integration sync progress trail.
--
--   integration_sync_logs.progress_log   The timestamped step-by-step trail of one sync run,
--                                        appended while the run is still going.
--
-- One nullable column, no table touched but this one, nothing dropped or renamed -- so this version
-- has no destructive gate and needs no observation window. Existing rows read back NULL, which is
-- correct: runs that finished before this version genuinely have no trail.
--
-- longtext rather than text: `text` caps at 64KB, and this is the one column here whose size follows
-- the size of the tenant rather than the shape of the schema. The trail is capped in code at 256KB
-- (IntegrationSyncProgressLog.MaxLength) so that the UPDATE writing it can never exceed
-- max_allowed_packet -- that write is also the one that records the run's outcome.

ALTER TABLE `integration_sync_logs`
    ADD COLUMN IF NOT EXISTS `progress_log` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;
