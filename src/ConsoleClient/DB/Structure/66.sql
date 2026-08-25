-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema — that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- Track 6 — Phase 1b: boolean width normalization (deferred from Phase 1).
-- tinyint(4) -> tinyint(1) for genuine booleans; C# properties changed sbyte -> bool.
ALTER TABLE `framework_controls` MODIFY COLUMN `deleted` tinyint(1) NOT NULL;

ALTER TABLE `comments` MODIFY COLUMN `IsAnonymous` tinyint(1) NOT NULL;

