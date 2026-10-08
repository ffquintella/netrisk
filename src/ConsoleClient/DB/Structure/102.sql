-- Concurrent renewal guard. One acceptance may have at most one successor; MariaDB unique indexes
-- permit multiple NULL values, so original (non-renewal) acceptances remain unrestricted.
--
-- Preserve history on upgrade. If an earlier race already produced two successors, fail before
-- changing any index and tell the operator what must be reconciled. The upgrade never chooses a
-- winner or deletes an acceptance decision.
SET @nr_ddl = IF(
    (SELECT COUNT(*) FROM (
        SELECT `renewed_from_id`
        FROM `risk_acceptances`
        WHERE `renewed_from_id` IS NOT NULL
        GROUP BY `renewed_from_id`
        HAVING COUNT(*) > 1
    ) AS `duplicate_renewals`) > 0,
    'SIGNAL SQLSTATE ''45000'' SET MESSAGE_TEXT = ''Cannot add uq_ra_renewed_from_id: duplicate renewal history exists; reconcile it explicitly without deleting acceptance evidence''',
    'DO 0');
PREPARE nr_ddl FROM @nr_ddl;
EXECUTE nr_ddl;
DEALLOCATE PREPARE nr_ddl;

-- The existing self-referencing FK requires an index, so create its unique replacement first.
CREATE UNIQUE INDEX IF NOT EXISTS `uq_ra_renewed_from_id`
    ON `risk_acceptances` (`renewed_from_id`);
ALTER TABLE `risk_acceptances` DROP INDEX IF EXISTS `idx_ra_renewed_from_id`;
