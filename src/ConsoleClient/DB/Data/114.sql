START TRANSACTION;

-- M58, T279-T280 (S39 D-15, D-16). Pure DML: a CREATE or ALTER here would commit the transaction out from under the
-- rest, and the db_version bump below would stop being the commit point.
--
-- Not backfilled, deliberately: incidents.resolved_at and assessment_runs.submitted_at / score stay NULL for existing
-- rows. Neither time was recorded (last_update and run_date are "last touched", in client-local time), and the index
-- keeps its documented fallback for NULL -- the frozen closure date, and run_date with the text-joined score.

-- T280 backfill: link each run answer to its option by text, only where exactly one option of the answer's question
-- carries that text, case aside -- the rule of Model.Assessments.AssessmentAnswerLink (pinned by
-- ConsoleClient.Tests/DB/AssessmentAnswerLinkBackfillTest). The text is the JSON string the desktop stores, or bare
-- text from older clients with the surrounding quotes trimmed. Rows that match no option or several stay NULL and are
-- listed by GET /Assessments/runs/answers/unlinked. Guarded by assessment_answer_id IS NULL: applying it again changes
-- nothing.
UPDATE `assessment_run_answers` ra
JOIN (
    SELECT a.`id` AS `run_answer_id`, MIN(o.`id`) AS `option_id`
    FROM `assessment_run_answers` a
    JOIN `assessment_answers` o
      ON o.`question_id` = a.`assessment_question_id`
     AND LOWER(CONVERT(o.`answer` USING utf8mb4)) COLLATE utf8mb4_bin =
         LOWER(CONVERT(CASE WHEN JSON_VALID(a.`answer_content_json`) AND JSON_TYPE(a.`answer_content_json`) = 'STRING'
                            THEN JSON_UNQUOTE(a.`answer_content_json`)
                            ELSE TRIM(BOTH '"' FROM a.`answer_content_json`) END USING utf8mb4)) COLLATE utf8mb4_bin
    WHERE a.`assessment_answer_id` IS NULL
      AND a.`answer_content_json` IS NOT NULL
      AND a.`answer_content_json` <> ''
    GROUP BY a.`id`
    HAVING COUNT(*) = 1
) m ON m.`run_answer_id` = ra.`id`
SET ra.`assessment_answer_id` = m.`option_id`
WHERE ra.`assessment_answer_id` IS NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261118162742_IncidentSeverityAndAssessmentSubmission', '10.0.13')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

update settings SET value = '114' where name = 'db_version';

COMMIT;
