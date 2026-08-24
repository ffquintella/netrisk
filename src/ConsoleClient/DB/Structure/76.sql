START TRANSACTION;
-- Track 2 milestone 2.4.3 — persisted task-dependency edges for incident response plans, and the
-- record of a deliberate override when a blocked task is completed anyway.
--
-- Until now the response Gantt derived its ordering from `ExecutionOrder` plus the `IsSequential`
-- flag, which can only express "this stage after that stage"; an author had no way to say that one
-- particular task waits on one other. These edges carry that, and the service validates the graph
-- is acyclic on save because a cycle makes the plan impossible to schedule.
CREATE TABLE `incident_response_plan_task_dependencies` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `task_id` int(11) NOT NULL,
    `depends_on_task_id` int(11) NOT NULL,
    `created_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`)
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- The same edge twice would double-count in the schedule.
CREATE UNIQUE INDEX `uq_irptd_task_depends_on`
    ON `incident_response_plan_task_dependencies` (`task_id`, `depends_on_task_id`);

CREATE INDEX `idx_irptd_depends_on_task_id`
    ON `incident_response_plan_task_dependencies` (`depends_on_task_id`);

-- Deleting a task takes its edges with it in both directions; a dangling edge would leave the
-- graph unschedulable.
ALTER TABLE `incident_response_plan_task_dependencies`
    ADD CONSTRAINT `fk_irptd_task_id` FOREIGN KEY (`task_id`)
        REFERENCES `incident_response_plan_tasks` (`Id`) ON DELETE CASCADE;

ALTER TABLE `incident_response_plan_task_dependencies`
    ADD CONSTRAINT `fk_irptd_depends_on_task_id` FOREIGN KEY (`depends_on_task_id`)
        REFERENCES `incident_response_plan_tasks` (`Id`) ON DELETE CASCADE;

-- Completing a task whose predecessor is still open requires a stated reason, and the columns
-- below are that audit record. Null on every task completed in the ordinary way.
ALTER TABLE `incident_response_plan_tasks` ADD `override_reason` text
    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `incident_response_plan_tasks` ADD `overridden_by_id` int(11) NULL;

ALTER TABLE `incident_response_plan_tasks` ADD `overridden_at` datetime NULL;

CREATE INDEX `idx_irpt_overridden_by_id`
    ON `incident_response_plan_tasks` (`overridden_by_id`);

ALTER TABLE `incident_response_plan_tasks`
    ADD CONSTRAINT `fk_irpt_overridden_by_id` FOREIGN KEY (`overridden_by_id`)
        REFERENCES `user` (`value`) ON DELETE SET NULL;

COMMIT;
