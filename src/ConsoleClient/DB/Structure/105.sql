-- T237 review (M52, S39 D-01): who created a report schedule. The scheduled report job reads every row of the report
-- under this user's entity scope as it stands at run time, never under the job host's global grant, so a scoped
-- user's schedule cannot mail another entity's incidents. EF migration 20261013120000_ReportScheduleCreator.
--
-- No transaction (MariaDB commits each DDL statement implicitly), and every statement guarded, so a retry after a
-- failure half-way re-applies only what is missing.
--
-- Nullable: a schedule created before this version has no recorded creator, and the job skips it until an
-- administrator saves it (which makes it theirs). No backfill -- the template owner is not the person who chose the
-- schedule's recipients, and guessing a creator would grant somebody's scope to a schedule they never configured.
-- ON DELETE SET NULL: a deleted creator's schedule stops rather than running as nobody.
ALTER TABLE `report_schedules` ADD COLUMN IF NOT EXISTS `created_by_id` int(11) NULL;

CREATE INDEX IF NOT EXISTS `idx_report_schedules_created_by_id` ON `report_schedules` (`created_by_id`);

ALTER TABLE `report_schedules` ADD CONSTRAINT `fk_report_schedules_created_by_id` FOREIGN KEY IF NOT EXISTS (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL;
