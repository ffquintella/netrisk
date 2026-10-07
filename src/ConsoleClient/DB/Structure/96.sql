-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema -- that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- Track 9 Stage 9.7 (M45, T182-T187, S48) -- tail statistics and portfolio: P95, CVaR, the loss
-- magnitude by form of loss, portfolio aggregation with declared correlation, and Gate B on the tail.
--
--   risk_loss_components       The Phase 3 loss magnitude of a risk by form of loss (response, recovery,
--                              productivity, revenue, liability, fine -- with its legal basis -- and
--                              reputation), each a calibrated per-event range.
--   risk_tail_statistics       E[L], P95 and CVaR95 with 95% confidence intervals, the probability of a
--                              loss year and the mean loss of a loss year, per risk and Monte Carlo run,
--                              with a copy of the inputs simulated.
--   risk_tail_components       Each component of a run: the range simulated and its contribution to the
--                              run's E[L] and (Euler) CVaR95.
--   risk_correlations          The declared correlation (0-1) between the annual losses of two scenarios.
--   risk_appetite_tail_limits  Gate B's monetary tolerances of an appetite, per scenario and portfolio.
--
-- New tables only: risk_scoring and risk_appetites are written from whole payloads, which would erase a
-- column an old client does not know (S48 D1). Created in reference order: risk_tail_statistics before
-- risk_tail_components.
--
-- Version 96 rather than the 96/97 S40 section 7.6 had reserved for M52 and M56/M57: this change
-- merged first, so S40 shifts its scripts to 97 and 98 (the rule #79, #80 and Stages 9.1-9.6 followed).

CREATE TABLE IF NOT EXISTS `risk_loss_components` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `risk_id` int(11) NOT NULL,
    `component` int(11) NOT NULL,
    `loss_min` double NOT NULL,
    `loss_most_likely` double NOT NULL,
    `loss_max` double NOT NULL,
    `basis` varchar(1000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    `updated_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_risk_loss_components_component` CHECK (`component` >= 1 AND `component` <= 7),
    CONSTRAINT `ck_risk_loss_components_fine_basis` CHECK (`component` <> 6 OR `basis` IS NOT NULL),
    CONSTRAINT `ck_risk_loss_components_range` CHECK (`loss_min` >= 0 AND `loss_min` <= `loss_most_likely` AND `loss_most_likely` <= `loss_max`),
    CONSTRAINT `fk_risk_loss_components_risk_id` FOREIGN KEY (`risk_id`) REFERENCES `risks` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_risk_loss_components_updated_by_id` FOREIGN KEY (`updated_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE UNIQUE INDEX IF NOT EXISTS `uq_risk_loss_components_risk_id_component` ON `risk_loss_components` (`risk_id`, `component`);
CREATE INDEX IF NOT EXISTS `idx_risk_loss_components_updated_by_id` ON `risk_loss_components` (`updated_by_id`);

CREATE TABLE IF NOT EXISTS `risk_tail_statistics` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `risk_id` int(11) NOT NULL,
    `run` int(11) NOT NULL,
    `iterations` int(11) NOT NULL,
    `seed` int(11) NOT NULL,
    `confidence_level` decimal(4,3) NOT NULL,
    `lef_min` double NOT NULL,
    `lef_most_likely` double NOT NULL,
    `lef_max` double NOT NULL,
    `magnitude_min` double NOT NULL,
    `magnitude_most_likely` double NOT NULL,
    `magnitude_max` double NOT NULL,
    `magnitude_source` int(11) NOT NULL,
    `mitigation_effectiveness` double NOT NULL,
    `expected_loss` double NOT NULL,
    `expected_loss_ci_low` double NOT NULL,
    `expected_loss_ci_high` double NOT NULL,
    `p95` double NOT NULL,
    `p95_ci_low` double NOT NULL,
    `p95_ci_high` double NOT NULL,
    `cvar95` double NOT NULL,
    `cvar95_ci_low` double NOT NULL,
    `cvar95_ci_high` double NOT NULL,
    `probability_of_loss` double NOT NULL,
    `conditional_loss` double NULL,
    `computed_at` datetime NOT NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_risk_tail_statistics_iterations` CHECK (`iterations` >= 1000 AND `iterations` <= 100000),
    CONSTRAINT `ck_risk_tail_statistics_magnitude_source` CHECK (`magnitude_source` >= 1 AND `magnitude_source` <= 2),
    CONSTRAINT `ck_risk_tail_statistics_mitigation_effectiveness` CHECK (`mitigation_effectiveness` >= 0 AND `mitigation_effectiveness` <= 1),
    CONSTRAINT `ck_risk_tail_statistics_probability_of_loss` CHECK (`probability_of_loss` >= 0 AND `probability_of_loss` <= 1),
    CONSTRAINT `ck_risk_tail_statistics_run` CHECK (`run` >= 1 AND `run` <= 2),
    CONSTRAINT `fk_risk_tail_statistics_risk_id` FOREIGN KEY (`risk_id`) REFERENCES `risks` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE UNIQUE INDEX IF NOT EXISTS `uq_risk_tail_statistics_risk_id_run` ON `risk_tail_statistics` (`risk_id`, `run`);

CREATE TABLE IF NOT EXISTS `risk_tail_components` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `tail_statistics_id` int(11) NOT NULL,
    `component` int(11) NOT NULL,
    `loss_min` double NOT NULL,
    `loss_most_likely` double NOT NULL,
    `loss_max` double NOT NULL,
    `expected_loss` double NOT NULL,
    `cvar95` double NOT NULL,
    `created_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_risk_tail_components_component` CHECK (`component` >= 1 AND `component` <= 7),
    CONSTRAINT `fk_risk_tail_components_tail_statistics_id` FOREIGN KEY (`tail_statistics_id`) REFERENCES `risk_tail_statistics` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE UNIQUE INDEX IF NOT EXISTS `uq_risk_tail_components_tail_statistics_id_component` ON `risk_tail_components` (`tail_statistics_id`, `component`);

CREATE TABLE IF NOT EXISTS `risk_correlations` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `risk_a_id` int(11) NOT NULL,
    `risk_b_id` int(11) NOT NULL,
    `coefficient` decimal(4,3) NOT NULL,
    `rationale` varchar(2000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    `updated_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_risk_correlations_coefficient` CHECK (`coefficient` >= 0 AND `coefficient` <= 1),
    CONSTRAINT `ck_risk_correlations_order` CHECK (`risk_a_id` < `risk_b_id`),
    CONSTRAINT `fk_risk_correlations_risk_a_id` FOREIGN KEY (`risk_a_id`) REFERENCES `risks` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_risk_correlations_risk_b_id` FOREIGN KEY (`risk_b_id`) REFERENCES `risks` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_risk_correlations_updated_by_id` FOREIGN KEY (`updated_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE UNIQUE INDEX IF NOT EXISTS `uq_risk_correlations_risk_a_id_risk_b_id` ON `risk_correlations` (`risk_a_id`, `risk_b_id`);
CREATE INDEX IF NOT EXISTS `idx_risk_correlations_risk_b_id` ON `risk_correlations` (`risk_b_id`);
CREATE INDEX IF NOT EXISTS `idx_risk_correlations_updated_by_id` ON `risk_correlations` (`updated_by_id`);

CREATE TABLE IF NOT EXISTS `risk_appetite_tail_limits` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `appetite_id` int(11) NOT NULL,
    `max_scenario_expected_loss` decimal(18,2) NULL,
    `max_scenario_p95` decimal(18,2) NULL,
    `max_scenario_cvar95` decimal(18,2) NULL,
    `max_portfolio_expected_loss` decimal(18,2) NULL,
    `max_portfolio_p95` decimal(18,2) NULL,
    `max_portfolio_cvar95` decimal(18,2) NULL,
    `rationale` varchar(2000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    `updated_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_risk_appetite_tail_limits_any` CHECK (`max_scenario_expected_loss` IS NOT NULL OR `max_scenario_p95` IS NOT NULL OR `max_scenario_cvar95` IS NOT NULL OR `max_portfolio_expected_loss` IS NOT NULL OR `max_portfolio_p95` IS NOT NULL OR `max_portfolio_cvar95` IS NOT NULL),
    CONSTRAINT `ck_risk_appetite_tail_limits_non_negative` CHECK ((`max_scenario_expected_loss` IS NULL OR `max_scenario_expected_loss` >= 0) AND (`max_scenario_p95` IS NULL OR `max_scenario_p95` >= 0) AND (`max_scenario_cvar95` IS NULL OR `max_scenario_cvar95` >= 0) AND (`max_portfolio_expected_loss` IS NULL OR `max_portfolio_expected_loss` >= 0) AND (`max_portfolio_p95` IS NULL OR `max_portfolio_p95` >= 0) AND (`max_portfolio_cvar95` IS NULL OR `max_portfolio_cvar95` >= 0)),
    CONSTRAINT `fk_risk_appetite_tail_limits_appetite_id` FOREIGN KEY (`appetite_id`) REFERENCES `risk_appetites` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_risk_appetite_tail_limits_updated_by_id` FOREIGN KEY (`updated_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE UNIQUE INDEX IF NOT EXISTS `uq_risk_appetite_tail_limits_appetite_id` ON `risk_appetite_tail_limits` (`appetite_id`);
CREATE INDEX IF NOT EXISTS `idx_risk_appetite_tail_limits_updated_by_id` ON `risk_appetite_tail_limits` (`updated_by_id`);
