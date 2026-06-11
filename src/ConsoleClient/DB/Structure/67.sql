START TRANSACTION;
-- Track 6 — Phase 2b: snake_case the last stray PascalCase column.
ALTER TABLE `comments` RENAME COLUMN `IsAnonymous` TO `is_anonymous`;

COMMIT;
