START TRANSACTION;

-- M53 security review (S40 §6.4, §11.4) -- the grant record and the profile editors start empty: no profile was ever
-- released, and a grant made before this version has no recorded author, which the approval reads as "not by the
-- approver" (S40 §6.4 refuses only a grant whose recorded author is the approver). Pure DML: a CREATE or ALTER here would
-- commit the transaction out from under the rest, and the db_version bump below would stop being the commit point.

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261016185505_RiskIndexApprovalEvidence', '10.0.13')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

update settings SET value = '109' where name = 'db_version';

COMMIT;
