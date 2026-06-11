START TRANSACTION;
-- Track 6 — Phase 1b: boolean width normalization (deferred from Phase 1).
-- tinyint(4) -> tinyint(1) for genuine booleans; C# properties changed sbyte -> bool.
ALTER TABLE `framework_controls` MODIFY COLUMN `deleted` tinyint(1) NOT NULL;

ALTER TABLE `comments` MODIFY COLUMN `IsAnonymous` tinyint(1) NOT NULL;

COMMIT;
