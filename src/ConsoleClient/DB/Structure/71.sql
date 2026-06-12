START TRANSACTION;
-- Track 6 — Phase 5: status type standardization (create-copy-coexist for risks.status).
-- Adds risks.status_id (int) alongside the legacy free-text `status` and backfills it from the known
-- status strings. The old `status` column is INTENTIONALLY retained: per the plan, the new column must
-- coexist for one release before the old one is dropped (never drop in the same release that creates it),
-- so risks.status is NOT dropped here (nor in this milestone's Phase 6b).
-- BiometricTransaction.TransactionResult also gained an explicit EF `int` conversion in this phase, but its
-- column is already `int NOT NULL`, so there is no DDL for it here (model/snapshot change only).
-- Mapping mirrors DAL.Enums.RiskStatus / Model.Risks.RiskHelper: New=0, Mitigation Planned=1,
-- Mgmt Reviewed=2, Closed=3. Rows whose `status` text is outside this set stay NULL (surfaced by the
-- phase's status-distribution census) rather than being misrepresented as New.
ALTER TABLE `risks` ADD COLUMN `status_id` int(11) NULL AFTER `status`;

UPDATE `risks` SET `status_id` = CASE `status`
    WHEN 'New' THEN 0
    WHEN 'Mitigation Planned' THEN 1
    WHEN 'Mgmt Reviewed' THEN 2
    WHEN 'Closed' THEN 3
    ELSE NULL
END;

COMMIT;
