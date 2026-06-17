using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.EntitiesDto;
using Model.Assessments;
using Model.DTO;

namespace ClientServices.Interfaces;

public interface IAssessmentsService
{
    
    /// <summary>
    /// Get the list of assessments from the server
    /// </summary>
    /// <returns></returns>
    Task<List<Assessment>?> GetAssessmentsAsync();
    
    /// <summary>
    /// Gets the runs for one assessment
    /// </summary>
    /// <param name="assessmentId"></param>
    /// <returns></returns>
    List<AssessmentRun>? GetAssessmentRuns(int assessmentId);
    
    /// <summary>
    /// Gets the answers for one run
    /// </summary>
    /// <param name="runId"></param>
    /// <returns></returns>
    List<AssessmentRunsAnswer>? GetAssessmentRunAnsers(int assessmentId, int runId);
    
    /// <summary>
    /// Creates a new Assessment
    /// </summary>
    /// <param name="assessment"></param>
    /// <returns>0 if ok, -1 if error</returns>
    Tuple<int, Assessment?> Create(Assessment assessment);
    
    /// <summary>
    /// Creates a new Assessment
    /// </summary>
    /// <param name="assessment"></param>
    /// <returns></returns>
    public Task<Tuple<int, Assessment?>> CreateAsync(Assessment assessment);
    
    /// <summary>
    /// Updates an existing Assessment
    /// </summary>
    /// <param name="assessment"></param>
    public Task<int> UpdateAsync(Assessment assessment);
    
    /// <summary>
    /// Creates a new AssessmentRun
    /// </summary>
    /// <param name="assessmentRun"></param>
    /// <returns></returns>
    AssessmentRun? CreateAssessmentRun(AssessmentRunDto assessmentRun);
    
    
    /// <summary>
    /// Updates an existing AssessmentRun
    /// </summary>
    /// <param name="assessmentRun"></param>
    void  UpdateAssessmentRun(AssessmentRunDto assessmentRun);
    
    /// <summary>
    /// Creates a new AssessmentRunAnswer
    /// </summary>
    /// <param name="answer"></param>
    /// <returns></returns>
    AssessmentRunsAnswer CreateRunAnswer(int assessmentId, AssessmentRunsAnswerDto answer);
    
    /// <summary>
    /// Saves the question on the server
    /// </summary>
    /// <param name="assessmentId">Assessment ID of the question</param>
    /// <param name="question">Question</param>
    /// <returns>0 if ok, -1 if error, 1 if alredy exists</returns>
    Tuple<int, AssessmentQuestion?> CreateQuestion(int assessmentId, AssessmentQuestion question);

    
    /// <summary>
    /// Saves the question on the server
    /// </summary>
    /// <param name="assessmentId">Assessment ID of the question</param>
    /// <param name="question">Question</param>
    /// <returns>0 if ok, -1 if error, 1 if alredy exists</returns>
    Task<Tuple<int, AssessmentQuestion?>> CreateQuestionAsync(int assessmentId, AssessmentQuestion question);

    /// <summary>
    /// Updates the apointed question
    /// </summary>
    /// <param name="assessmentId">Assessment ID</param>
    /// <param name="question">The question object</param>
    /// <returns>0 if ok; -1 if internal error, 1 if not found</returns>
    Tuple<int, AssessmentQuestion?> UpdateQuestion(int assessmentId, AssessmentQuestionDto question);
    
    /// <summary>
    /// Updates the apointed question
    /// </summary>
    /// <param name="assessmentId">Assessment ID</param>
    /// <param name="question">The question object</param>
    /// <returns>0 if ok; -1 if internal error, 1 if not found</returns>
    Task<Tuple<int, AssessmentQuestion?>> UpdateQuestionAsync(int assessmentId, AssessmentQuestionDto question);
    
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
    /// Deletes all answers for one run
    /// </summary>
    /// <param name="assessmentId"></param>
    /// <param name="runId"></param>
    public void DeleteAllAnswers(int assessmentId, int runId);


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
    /// Deletes one assessment run
    /// </summary>
    /// <param name="assessmentId"></param>
    /// <param name="runId"></param>
    void DeleteRun(int assessmentId, int runId);

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

    /// <summary>
    /// Gets the visible questions for a page of a run, with the server evaluating the
    /// conditional show/hide logic against the run's saved draft answers.
    /// </summary>
    /// <param name="runId">The assessment run id</param>
    /// <param name="pageNumber">The page (group) number</param>
    /// <returns>The visible questions for the page or null on error</returns>
    Task<List<AssessmentQuestion>?> GetVisibleQuestionsForPageAsync(int runId, int pageNumber);

    /// <summary>
    /// Gets the saved draft answers for a run (used to resume an in-progress run).
    /// </summary>
    /// <param name="runId">The assessment run id</param>
    /// <returns>The list of draft answers or null on error</returns>
    Task<List<AssessmentRunAnswer>?> GetDraftAnswersAsync(int runId);

    /// <summary>
    /// Saves (upserts) a single draft answer for a question in a run. Used by the
    /// viewer's auto-save.
    /// </summary>
    /// <param name="runId">The assessment run id</param>
    /// <param name="questionId">The question id being answered</param>
    /// <param name="answerContentJson">The answer content, JSON-encoded</param>
    /// <returns>The saved draft answer or null on error</returns>
    Task<AssessmentRunAnswer?> SaveDraftAnswerAsync(int runId, int questionId, string answerContentJson);

    /// <summary>
    /// Dry-run validation of an assessment template file (JSON or Excel). Returns the
    /// summary (pages, questions, warnings, row-level errors) without importing anything.
    /// </summary>
    /// <param name="filePath">The local path of the template file</param>
    /// <param name="assessmentName">Optional name override (used for Excel imports)</param>
    /// <returns>The import preview, or null on a communication error</returns>
    Task<AssessmentImportPreview?> PreviewTemplateAsync(string filePath, string? assessmentName);

    /// <summary>
    /// Imports an assessment template from a JSON or Excel (.xlsx) file.
    /// </summary>
    /// <param name="filePath">The local path of the template file</param>
    /// <param name="assessmentName">Optional name override (used for Excel imports)</param>
    /// <returns>0 and the created assessment if ok; -1 and null on error</returns>
    Task<Tuple<int, Assessment?>> ImportTemplateAsync(string filePath, string? assessmentName);
}