START TRANSACTION;

ALTER TABLE `risks` DROP CONSTRAINT  IF EXISTS `FK_risks_IncidentResponsePlans_IncidentResponsePlanId`;

COMMIT;