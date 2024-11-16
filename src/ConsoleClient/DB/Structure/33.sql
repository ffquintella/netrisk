START TRANSACTION;

CREATE FULLTEXT INDEX `idx_action_message` ON `nr_actions` (`Message`);

COMMIT;