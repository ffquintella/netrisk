START TRANSACTION;

-- Track 9, Stage 9.1 (S41, M39) -- the risk linkage chain.
--
-- Pure DML. A single CREATE or ALTER here would implicitly commit the transaction out from under the
-- rest of the script, and the db_version bump below would stop being the commit point that makes a
-- failed Data script roll back whole.

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261006132004_Track9RiskChainLinks', '10.0.12')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

-- create-copy-coexist: every risk_to_entity row whose target is of a chain type becomes a link of
-- origin Legacy (2), at the level the target's type implies. Units, people, teams and classification
-- levels are scope or organisation, not identification, and are not copied. risks.entity_id is not
-- copied either: it is the scope column (S41 §11, D4).
--
-- The NOT EXISTS makes the copy safe to re-run: a row already linked, by either origin, is skipped.
INSERT INTO `risk_chain_links` (`risk_id`, `chain_level`, `entity_id`, `host_id`, `origin`, `created_at`, `created_by_id`, `updated_at`)
SELECT rte.`risk_id`,
       CASE e.`DefinitionName`
           WHEN 'strategicObjective' THEN 1
           WHEN 'businessProcess' THEN 2 WHEN 'activity' THEN 2
           WHEN 'itService' THEN 3
           WHEN 'organizationData' THEN 4 WHEN 'organizationDataGroup' THEN 4
           WHEN 'application' THEN 5 WHEN 'applicationModule' THEN 5
       END,
       rte.`entity_id`, NULL, 2, UTC_TIMESTAMP(), NULL, NULL
FROM `risk_to_entity` rte
JOIN `entities` e ON e.`Id` = rte.`entity_id`
WHERE e.`DefinitionName` IN ('strategicObjective', 'businessProcess', 'activity', 'itService',
                             'organizationData', 'organizationDataGroup', 'application', 'applicationModule')
  AND NOT EXISTS (SELECT 1 FROM `risk_chain_links` l
                   WHERE l.`risk_id` = rte.`risk_id` AND l.`entity_id` = rte.`entity_id`);

update settings SET value = '88' where name = 'db_version';

COMMIT;
