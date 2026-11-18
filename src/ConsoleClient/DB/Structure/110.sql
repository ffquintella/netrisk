-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so this script carries no
-- transaction; every statement is guarded instead, and applying the version again converges on the
-- same schema.

-- M55, T264 (S40 §6.7, §8.5) -- the justification of each risk-context batch (PUT /Entities/RiskContext).
-- EF migration 20261118110526_EntityRiskContextNotes.

-- New table:
--   entity_risk_context_notes  one row per entity a batch changed, all with the batch's justification and batch_id;
--                              batch_id is the audit_logs.correlation_id of the batch's save, so the entity history
--                              joins each audit row to its reason; insert-only; the entity is CASCADE (the note goes
--                              with its entity, as the property rows do), the author SET NULL (the evidence stays)

CREATE TABLE IF NOT EXISTS `entity_risk_context_notes` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `batch_id` varchar(36) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `entity_id` int(11) NOT NULL,
    `justification` varchar(2000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_entity_risk_context_notes_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_entity_risk_context_notes_entity_id` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX IF NOT EXISTS `idx_entity_risk_context_notes_batch_id` ON `entity_risk_context_notes` (`batch_id`);

CREATE INDEX IF NOT EXISTS `idx_entity_risk_context_notes_created_by_id` ON `entity_risk_context_notes` (`created_by_id`);

CREATE INDEX IF NOT EXISTS `idx_entity_risk_context_notes_entity_id` ON `entity_risk_context_notes` (`entity_id`);
