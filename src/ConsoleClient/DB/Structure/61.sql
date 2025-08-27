START TRANSACTION;
CREATE UNIQUE INDEX `idx_biometic_id` ON `BiometricTransaction` (`TransactionId`);

COMMIT;