using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Entities;

namespace ClientServices.Interfaces;

public interface IAssessmentsService
{
    /// <summary>
    /// Get the list of assessments frome the server
    /// </summary>
    /// <returns>The list or null</returns>
    List<Assessment>? GetAssessments();
    
    /// <summary>
    /// Creates a new Assessment
    /// </summary>
    /// <param name="assessment"></param>
    /// <returns>0 if ok, -1 if error</returns>
    Tuple<int, Assessment?> Create(Assessment assessment);
    
    
    /// <summary>
    /// Saves the question on the server
    /// </summary>
    /// <param name="assessmentId">Assessment ID of the question</param>
    /// <param name="question">Question</param>
    /// <returns>0 if ok, -1 if error, 1 if alredy exists</returns>
    Tuple<int, AssessmentQuestion?> CreateQuestion(int assessmentId, AssessmentQuestion question);


    /// <summary>
    /// Updates the apointed question
    /// </summary>
    /// <param name="assessmentId">Assessment ID</param>
    /// <param name="question">The question object</param>
    /// <returns>0 if ok; -1 if internal error, 1 if not found</returns>
    Tuple<int, AssessmentQuestion?> UpdateQuestion(int assessmentId, AssessmentQuestion question);
    /// <summary>
    /// Creates new answers on the server
    /// </summary>
    /// <param name="assessmentId">Assessment ID of the question</param>
    /// <param name="questionId">Question ID of the question</param>
    /// <param name="answers">List of assessment answers</param>
    /// <returns>0 if ok, -1 if error</returns>
    Tuple<int, List<AssessmentAnswer>?> CreateAnswers(int assessmentId,
        int questionId,
        List<AssessmentAnswer> answers);
    
    /// <summary>
    /// Updates existing answers on the server
    /// </summary>
    /// <param name="assessmentId">Assessment ID of the question</param>
    /// <param name="questionId">Question ID of the question</param>
    /// <param name="answers">List of assessment answers</param>
    /// <returns>0 if ok, -1 if error</returns>
    Tuple<int, List<AssessmentAnswer>?> UpdateAnswers(int assessmentId,
        int questionId,
        List<AssessmentAnswer> answers);


    /// <summary>
    /// Delete a list of answers
    /// </summary>
    /// <param name="assessmentId">Assessment ID of the answer</param>
    /// <param name="questionId">Question Id of the answer</param>
    /// <param name="answers">list of answers</param>
    /// <returns>0 if ok; -1 if error.</returns>
    int DeleteAnswers(int assessmentId,
        int questionId,
        List<AssessmentAnswer> answers);
    
    /// <summary>
    /// Deletes one assessment
    /// </summary>
    /// <param name="assessmentId">The asssessment id</param>
    /// <returns>0 if ok, -1 if error</returns>
    int Delete(int assessmentId);

    /// <summary>
    /// Deletes the assessment question
    /// </summary>
    /// <param name="assessmentId">The asssessment id</param>
    /// <param name="assessmentQuestionId">The assessment question ID</param>
    /// <returns>0 if ok, -1 if error;</returns>
    int DeleteQuestion(int assessmentId, int assessmentQuestionId);
    
    /// <summary>
    /// Get the assessment questions from the server
    /// </summary>
    /// <param name="assessmentId"></param>
    /// <returns>The question list or null if not found</returns>
    List<AssessmentQuestion>? GetAssessmentQuestions(int assessmentId);

    /// <summary>
    /// Get the assessment answers from the server
    /// </summary>
    /// <param name="assessmentId"></param>
    /// <returns></returns>
    List<AssessmentAnswer>? GetAssessmentAnswers(int assessmentId);
}