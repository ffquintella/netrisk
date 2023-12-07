
ALTER TABLE `assessment_questions`
    ADD CONSTRAINT `fk_assessment_question` FOREIGN KEY (`assessment_id`) REFERENCES `assessments` (`id`) ON DELETE CASCADE;

ALTER TABLE `assessment_answers`
    ADD CONSTRAINT `fk_question_answer` FOREIGN KEY (`question_id`) REFERENCES `assessment_questions` (`id`) ON DELETE CASCADE,
    ADD CONSTRAINT `fk_assessment_answer` FOREIGN KEY (`assessment_id`) REFERENCES `assessments` (`id`) ON DELETE CASCADE;