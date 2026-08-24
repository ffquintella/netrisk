START TRANSACTION;
-- Widen `processed_sync_actions`.`client_action_id` from char(36) to varchar(36).
--
-- This is a toolchain fix, not a modelling one. A string column whose store type is `char(n)` makes
-- EF Core 10's ElementMappingConvention treat the property — a string being an IEnumerable<char> —
-- as a primitive collection of char. The char element mapping does not exist in the MySQL provider,
-- so the model build dies with a NullReferenceException deep inside the type mapping source, and
-- every `dotnet ef migrations script` and every schema-consistency check fails with it.
--
-- The model previously dodged this by expressing the column as max-length plus fixed-length rather
-- than as an explicit store type, which works in OnModelCreating — but the generated model snapshot
-- re-resolves the store type and writes `HasColumnType("char(36)")` back, so the trap re-armed
-- itself on every migration and had to be patched out by hand each time.
--
-- char(36) and varchar(36) hold the same 36-character client action id. The only difference is
-- trailing-space padding, which a UUID string never has, so no stored value changes meaning.
ALTER TABLE `processed_sync_actions`
    MODIFY COLUMN `client_action_id` varchar(36)
        CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL;

COMMIT;
