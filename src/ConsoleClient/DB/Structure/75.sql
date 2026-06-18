START TRANSACTION;
-- Idempotency ledger for website-originated sync actions. The website is decoupled from the
-- main database and ships visitor actions (fix reports, comments, password changes, link
-- deletes, IRP task outcomes) back through a signed periodic sync under at-least-once
-- delivery; recording each client_action_id here lets the server safely skip re-applies.
CREATE TABLE `processed_sync_actions` (
    `client_action_id` char(36) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `action_type` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `applied_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`client_action_id`)
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

COMMIT;
