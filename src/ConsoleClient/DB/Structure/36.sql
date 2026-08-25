-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema — that, and not
-- a transaction, is what makes the upgrade safe to retry.

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `HasBeenTested` tinyint(1) NOT NULL DEFAULT 0;

update settings SET value = '35' where name = 'db_version';

