-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema -- that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- GitHub #79 -- classify a vulnerability as in a server or in an application.
--
--   vulnerabilities.source_type   int + C# enum VulnerabilitySourceType: 0 Unknown, 1 Server,
--                                 2 Application. NOT NULL DEFAULT 0, so every existing row stays valid
--                                 and reads as unclassified -- the truth -- rather than as a guess.
--
-- Version 91 rather than the 91/92 S40 §7.6 had reserved for M52 and M56/M57: this change merged
-- first, so S40 shifts its scripts to 92 and 93 (the same rule Stages 9.1, 9.2 and 9.3 followed).

ALTER TABLE `vulnerabilities` ADD COLUMN IF NOT EXISTS `source_type` int(11) NOT NULL DEFAULT 0;
