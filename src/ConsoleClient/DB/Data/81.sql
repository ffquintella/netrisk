START TRANSACTION;

-- Track 8 milestone 8.6 -- the business risk acceptance portal.

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260826163134_Track8ReviewPortalSchema', '10.0.11')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

-- 8.6.2 -- the permission the portal gates on. Deliberately a new one rather than reusing
-- `riskmanagement`: the audience is business reviewers who should be able to decide their entity's
-- risks and nothing else, and granting them the risk-management permission to reach the portal would
-- hand them the whole register.
-- No explicit id. `permissions.id` is auto_increment and `key` is the unique column, so INSERT
-- IGNORE keyed on the natural key is both idempotent and collision-free.
--
-- An earlier draft of this script named id 50 with ON DUPLICATE KEY UPDATE, which was wrong in a way
-- that only shows up on a real database: 50 is already `incident-response-plans` (seeded by a later
-- Data script than the one that stops at 49), so the upsert silently *renamed that permission*
-- instead of adding this one. Caught by applying all 82 versions to a MariaDB container; the guard
-- that keeps it from coming back is ConsoleClient.Tests.DB.SchemaUpgradeFilesTest.
INSERT IGNORE INTO `permissions` (`key`, `name`, `description`, `order`)
VALUES ('business_risk_review', 'Able to review and decide the risks of the business entities they are appointed to',
        'Grants access to the Business Risk Acceptance Portal. A holder sees only the entities they have been appointed a risk reviewer for, and the segregation-of-duties rules still apply -- they cannot decide a risk they submitted, own or manage.', 1);

-- 8.6.3 -- campaign generation. Quarterly is the default the spec names; the due window is how long
-- a reviewer has before the campaign is overdue and starts re-notifying.
INSERT INTO `settings` (`name`, `value`) VALUES ('risk_review_campaigns_enabled', 'true')
ON DUPLICATE KEY UPDATE `value` = `value`;
INSERT INTO `settings` (`name`, `value`) VALUES ('risk_review_campaign_cadence_months', '3')
ON DUPLICATE KEY UPDATE `value` = `value`;
INSERT INTO `settings` (`name`, `value`) VALUES ('risk_review_campaign_due_days', '30')
ON DUPLICATE KEY UPDATE `value` = `value`;

update settings SET value = '81' where name = 'db_version';

COMMIT;
