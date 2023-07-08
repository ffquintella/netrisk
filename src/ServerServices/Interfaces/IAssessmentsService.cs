using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IAssessmentsService
{
    /// <summary>
    /// List all assessments
    /// </summary>
    /// <returns>List of Assessments</returns>
    List<Assessment> List();
    
    /// <summary>
    /// Returns one assessment by id
    /// </summary>
    /// <param name="id">Assessment Id</param>
    /// <returns>Assessment Object Or Null if not found</returns>
    Assessment? Get(int id);
    
    /// <summary>
    /// Returns the list of answers from one assessment by id
    /// </summary>
    /// <param name="id">Assessment Id</param>
    /// <returns>AssessmentAnswer List Or Null if not found</returns>
    List<AssessmentAnswer>? GetAnswers(int id);


    /// <summary>
    /// Get all answers from one question
    /// </summary>
    /// <param name="questionId">Question id</param>
    /// <returns>List of answers </returns>
    List<AssessmentAnswer>? GetQuestionAnswers(int questionId);
    
    /// <summary>
    /// Gets one answer by it's identifiers
    /// </summary>
    /// <param name="assessmentId">Assessment Id</param>
    /// <param name="questionId">Question Id</param>
    /// <param name="answerText">The answer text</param>
    /// <returns>answer if found null if not</returns>
    AssessmentAnswer? GetAnswer(int assessmentId, int questionId, string answerText);

    /// <summary>
    /// Get assessment Answer by it
    /// </summary>
    /// <param name="answerId">answer id</param>
    /// <returns>Answer if ok null if error</returns>
    AssessmentAnswer? GetAnswerById(int answerId);
    
    /// <summary>
    /// Returns the list of questions from one assessment by id
    /// </summary>
    /// <param name="id">Assessment Id</param>
    /// <returns>AssessmentQuestion List Or Null if not found</returns>
    List<AssessmentQuestion>? GetQuestions(int id);
    
    /// <summary>
    /// Finds the question with the given id and question text
    /// </summary>
    /// <param name="id">Assessment ID</param>
    /// <param name="question">Question text</param>
    /// <returns>AssessmentQuestion or null</returns>
    AssessmentQuestion? GetQuestion(int id, string question);
    
    
    /// <summary>
    /// Finds the question using it's ID
    /// </summary>
    /// <param name="questionId">Question ID</param>
    /// <returns>AssessmentQuestion or null</returns>
    AssessmentQuestion? GetQuestionById(int questionId);
    
    /// <summary>
    /// Finds the question with the given id and question text
    /// </summary>
    /// <param name="id">Assessment ID</param>
    /// <param name="questionId">Question Id</param>
    /// <returns>AssessmentQuestion or null</returns>
    AssessmentQuestion? GetQuestionById(int id, int questionId);
    
    /// <summary>
    /// Saves the question on the database
    /// </summary>
    /// <param name="question">Question</param>
    /// <returns></returns>
    AssessmentQuestion? SaveQuestion(AssessmentQuestion question);
    
    /// <summary>
    /// Saves the answer on the database
    /// </summary>
    /// <param name="answer">Assessment Answer</param>
    /// <returns></returns>
    AssessmentAnswer? SaveAnswer(AssessmentAnswer answer);

    /// <summary>
    /// Delete Assessment Answer
    /// </summary>
    /// <param name="answer">the object to delete</param>
    /// <returns>0 if ok; -1 if error</returns>
    int DeleteAnswer(AssessmentAnswer answer);

    /// <summary>
    /// Deletes the assessment question
    /// </summary>
    /// <param name="question">the question object</param>
    /// <returns>0 if ok, -1 if error</returns>
    int DeleteQuestion(AssessmentQuestion question);
    /// <summary>
    /// Creates a new assessment on the database
    /// </summary>
    /// <param name="assessment"></param>
    /// <returns>-1 if error, 0 if ok, 1 if already exists</returns>
    Tuple<int,Assessment?> Create(Assessment assessment);

    /// <summary>
    /// Deletes the assessment specified by id
    /// </summary>
    /// <param name="assessment">assessment</param>
    /// <returns>-1 if error, 0 if ok</returns>
    int Delete(Assessment assessment);
}