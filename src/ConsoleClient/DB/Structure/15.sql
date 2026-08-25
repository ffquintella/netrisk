
ALTER TABLE `assessment_runs`
    ADD COLUMN IF NOT EXISTS `Comments` text NULL AFTER `Status`;