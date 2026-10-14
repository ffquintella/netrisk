START TRANSACTION;

-- M52, the ICR foundation (S39 D-02, D-04, D-05) -- the data backfills of T238, T239 and T240.
--
-- Pure DML. A single CREATE or ALTER here would implicitly commit the transaction out from under the
-- rest of the script, and the db_version bump below would stop being the commit point that makes a
-- failed Data script roll back whole. There is no DDL in this version (Structure/103.sql is empty, as
-- 32, 44, 46, 49 and 55 are) and no EF migration: the model does not change.
--
-- Every statement is guarded by the very state it corrects (status_id = 1, entity_id IS NULL,
-- value = 10.1), so applying it again changes nothing.

-- ---------------------------------------------------------------------------------------------
-- T239 (D-04) -- findings the desktop closed or rejected while the lifecycle stayed Active.
--
-- The desktop used to close and reject findings by writing only the legacy workflow column
-- (`Status`, IntStatus), so status_id kept saying Active and the finding counted as open for the
-- API, the SLA and the risk index. From this release the desktop routes those actions through the
-- lifecycle; this moves the rows it closed before.
--
-- The mapping is Model.Status.LegacyFindingStatus.ClosingTargets, and its keys are exactly
-- Model.Status.ClosedStatuses (ConsoleClient.Tests/DB/SchemaUpgradeBackfillTest pins both CASEs
-- below to it). It extends Structure/77.sql's with the values 77 left Active:
--   7 Mitigated     <- 4 Closed, 8 Mitigated, 25 Fixed, 26 Solved, 31 FixVerified, 47 Completed,
--                      27 Retired, 37 Deleted, 49 Cancelled, 29 Outdated
--   3 FalsePositive <- 11 Rejected
--   4 OutOfScope    <- 6 NotRelevant, 22 FixNotRequired
--   5 Duplicate     <- 12 Duplicated (the canonical finding is unknown, as it was in 77)
--
-- A finding whose lifecycle was ever moved back to Active from another state -- a scanner
-- regression, a manual reopen, an expired or revoked acceptance -- is left alone even though its
-- workflow column still says closed: the lifecycle's reopening is the later, recorded decision, and
-- the desktop close that may have followed it carries no timestamp comparable with the history's
-- (UTC there, client-local in nr_actions). Leaving it open is the safe direction.
--
-- Every moved finding gets its history row (source 3 = Job, no user), written first with the same
-- predicate and the same CASE, so status and timeline cannot disagree: a status change without a
-- history row is indistinguishable from tampering (FindingLifecycleService).
-- ---------------------------------------------------------------------------------------------
INSERT INTO `finding_status_history`
    (`vulnerability_id`, `from_status_id`, `to_status_id`, `user_id`, `changed_at`, `source`, `justification`)
SELECT v.`Id`,
       1,
       CASE v.`Status`
           WHEN 4 THEN 7 WHEN 8 THEN 7 WHEN 25 THEN 7 WHEN 26 THEN 7 WHEN 31 THEN 7 WHEN 47 THEN 7
           WHEN 27 THEN 7 WHEN 37 THEN 7 WHEN 49 THEN 7 WHEN 29 THEN 7
           WHEN 11 THEN 3
           WHEN 6 THEN 4 WHEN 22 THEN 4
           WHEN 12 THEN 5
       END,
       NULL,
       UTC_TIMESTAMP(),
       3,
       CONCAT('T239 backfill: closed in the desktop register with workflow status ', v.`Status`,
              ' while the lifecycle stayed Active.')
FROM `vulnerabilities` v
WHERE v.`status_id` = 1
  AND v.`Status` IN (4, 8, 25, 26, 31, 47, 27, 37, 49, 29, 11, 6, 22, 12)
  AND NOT EXISTS (SELECT 1 FROM `finding_status_history` h
                   WHERE h.`vulnerability_id` = v.`Id` AND h.`to_status_id` = 1 AND h.`from_status_id` IS NOT NULL);

UPDATE `vulnerabilities` v
SET v.`status_id` = CASE v.`Status`
                        WHEN 4 THEN 7 WHEN 8 THEN 7 WHEN 25 THEN 7 WHEN 26 THEN 7 WHEN 31 THEN 7 WHEN 47 THEN 7
                        WHEN 27 THEN 7 WHEN 37 THEN 7 WHEN 49 THEN 7 WHEN 29 THEN 7
                        WHEN 11 THEN 3
                        WHEN 6 THEN 4 WHEN 22 THEN 4
                        WHEN 12 THEN 5
                    END
WHERE v.`status_id` = 1
  AND v.`Status` IN (4, 8, 25, 26, 31, 47, 27, 37, 49, 29, 11, 6, 22, 12)
  AND NOT EXISTS (SELECT 1 FROM `finding_status_history` h
                   WHERE h.`vulnerability_id` = v.`Id` AND h.`to_status_id` = 1 AND h.`from_status_id` IS NOT NULL);

-- ---------------------------------------------------------------------------------------------
-- T240 (D-05) -- risks.entity_id from risk_to_entity.
--
-- risks.entity_id is the scope column (visibility, appetite, campaigns); risk_to_entity is what the
-- desktop writes. Version 74 created the column without a backfill, so every risk linked only
-- through risk_to_entity has a NULL scope: invisible to every scoped user and measured against the
-- global appetite. Scenario attribution for the risk index is the deduplicated union of the two
-- (ServerServices.Attribution.ScenarioAttribution); this fills the scope column it depends on.
--
-- Which entity, deterministically, when a risk links to several:
--   1. only organisational entities qualify -- subOrganizationUnit, organizationUnit, organization.
--      A process, application, service, data record, objective, person or team is identification,
--      not scope (S41 D4, Data/88.sql), and putting one in the scope column would mix the two. A risk
--      linked only to those keeps a NULL scope and is still attributed to them through the union.
--   2. the most specific qualifying type wins: subOrganizationUnit, then organizationUnit, then
--      organization -- the narrowest unit the risk was linked to.
--   3. ties go to the lowest entity id.
-- A risk that already has an entity_id is never touched: an explicit scope is a decision, not drift.
--
-- Giving a risk a scope makes it visible to the users scoped to that entity, which it was not before;
-- nothing becomes invisible to anyone. Each change gets a field-level audit row (Track 8, 8.4.1),
-- written first with the same predicate and the same choice, as the interceptor would write it.
-- ---------------------------------------------------------------------------------------------
INSERT INTO `audit_logs`
    (`entity_type`, `entity_id`, `field`, `old_value`, `new_value`, `action`, `user_id`, `actor`, `occurred_at`, `correlation_id`)
SELECT 'Risk',
       r.`id`,
       'EntityId',
       NULL,
       CAST((SELECT rte.`entity_id`
             FROM `risk_to_entity` rte
             JOIN `entities` e ON e.`Id` = rte.`entity_id`
             WHERE rte.`risk_id` = r.`id`
               AND e.`DefinitionName` IN ('subOrganizationUnit', 'organizationUnit', 'organization')
             ORDER BY FIELD(e.`DefinitionName`, 'subOrganizationUnit', 'organizationUnit', 'organization'),
                      rte.`entity_id`
             LIMIT 1) AS CHAR),
       2,
       NULL,
       'schema-upgrade',
       UTC_TIMESTAMP(),
       'T240-risk-scope-backfill'
FROM `risks` r
WHERE r.`entity_id` IS NULL
  AND EXISTS (SELECT 1
              FROM `risk_to_entity` rte
              JOIN `entities` e ON e.`Id` = rte.`entity_id`
              WHERE rte.`risk_id` = r.`id`
                AND e.`DefinitionName` IN ('subOrganizationUnit', 'organizationUnit', 'organization'));

UPDATE `risks` r
SET r.`entity_id` = (SELECT rte.`entity_id`
                     FROM `risk_to_entity` rte
                     JOIN `entities` e ON e.`Id` = rte.`entity_id`
                     WHERE rte.`risk_id` = r.`id`
                       AND e.`DefinitionName` IN ('subOrganizationUnit', 'organizationUnit', 'organization')
                     ORDER BY FIELD(e.`DefinitionName`, 'subOrganizationUnit', 'organizationUnit', 'organization'),
                              rte.`entity_id`
                     LIMIT 1)
WHERE r.`entity_id` IS NULL
  AND EXISTS (SELECT 1
              FROM `risk_to_entity` rte
              JOIN `entities` e ON e.`Id` = rte.`entity_id`
              WHERE rte.`risk_id` = r.`id`
                AND e.`DefinitionName` IN ('subOrganizationUnit', 'organizationUnit', 'organization'));

-- ---------------------------------------------------------------------------------------------
-- T238 leftover (D-02) -- a reachable Very High.
--
-- Data/1.sql seeded Very High at 10.1, above the 0-10 score range, so Tools.Risks.RiskBand.Faixa
-- (inclusive lower bounds) could never return it from the register limits and the 30-day cadence
-- never applied. 9.0 is S39 section 4.5's register lower bound. Only the untouched seed row moves: an
-- operator who already set their own Very High keeps it.
-- ---------------------------------------------------------------------------------------------
UPDATE `risk_levels`
SET `value` = 9.0
WHERE `name` = 'Very High' AND `value` = 10.1;

update settings SET value = '103' where name = 'db_version';

COMMIT;
