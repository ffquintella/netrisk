START TRANSACTION;

-- Track 8 milestone 8.6 -- the business risk acceptance portal.

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260826163134_Track8ReviewPortalSchema', '10.0.11')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

-- 8.6.2 -- the permission the portal gates on. Deliberately a new one rather than reusing
-- `riskmanagement`: the audience is business reviewers who should be able to decide their entity's
-- risks and nothing else, and granting them the risk-management permission to reach the portal would
-- hand them the whole register.
INSERT INTO `permissions` (`id`, `key`, `name`, `description`, `order`)
VALUES (50, 'business_risk_review', 'Able to review and decide the risks of the business entities they are appointed to',
        'Grants access to the Business Risk Acceptance Portal. A holder sees only the entities they have been appointed a risk reviewer for, and the segregation-of-duties rules still apply -- they cannot decide a risk they submitted, own or manage.', 1)
ON DUPLICATE KEY UPDATE `name` = VALUES(`name`), `description` = VALUES(`description`);

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
