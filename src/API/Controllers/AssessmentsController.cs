using System;
using System.Collections.Generic;
using DAL.Entities;
using DAL.EntitiesDto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Mapster;
using Model.DTO;
using ServerServices;
using ServerServices.Interfaces;

namespace API.Controllers;

[Authorize(Policy = "RequireAssessmentAccess")]
[ApiController]
[Route("[controller]")]
public class AssessmentsController : ApiBaseController
{

    private IAssessmentsService _assessmentsService;
    public AssessmentsController(Serilog.ILogger logger,
        IAssessmentsService assessmentsService,
        IHttpContextAccessor httpContextAccessor,
        IUsersService usersService) : base(logger, httpContextAccessor, usersService)
    {
        _assessmentsService = assessmentsService;
    }
  
    /// <summary>
    /// List all assessments
    /// </summary>
    /// <returns></returns>
    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Assessment>))]
    public ActionResult<List<Assessment>> GetAll()
    {

        try
        {
            var assessments = _assessmentsService.List();
            Logger.Debug("Listing all assessments");
            return assessments;

        }catch(Exception ex)
        {
            Logger.Error(ex, "Error listing all assessments");
            return StatusCode(500, "Error listing all assessments");
        }

    }
    
    /// <summary>
    /// Gets the detail of the assessment
    /// </summary>
    /// <param name="assessmentId">The ID of the assessment</param>
    /// <returns></returns>
    [HttpGet]
    [Route("{assessmentId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Assessment))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(string))]
    public ActionResult<Assessment> GetAssessment(int assessmentId)
    {

        try
        {
            Logger.Debug("Searching assessment with id {id}", assessmentId);
            var assessment = _assessmentsService.Get(assessmentId);
            if (assessment == null)
            {
                Logger.Error("Assessment with id {id} not found", assessmentId);
                return NotFound("Assessment not found");
            }
            
            return assessment;

        }catch(Exception ex)
        {
            Logger.Error(ex, "Error finding assessment");
            return StatusCode(500, "Error finding assessment");
        }

    }
    
    /// <summary>
    /// Gets the list of runs for this assessment
    /// </summary>
    /// <param name="assessmentId">The ID of the assessment</param>
    /// <returns></returns>
    [HttpGet]
    [Route("{assessmentId}/runs")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Assessment))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(string))]
    public ActionResult<List<AssessmentRun>> GetAssessmentRuns(int assessmentId)
    {

        try
        {
            Logger.Debug("Searching assessment with id {id}", assessmentId);
            var assessmentRuns = _assessmentsService.GetRuns(assessmentId);
            if (assessmentRuns == null)
            {
                Logger.Error("Assessment with id {id} not found", assessmentId);
                return NotFound("Assessment not found");
            }
            
            return assessmentRuns;

        }catch(Exception ex)
        {
            Logger.Error(ex, "Error finding assessment");
            return StatusCode(500, "Error finding assessment");
        }

    }
    
    
    
    /// <summary>
    /// Creates an assessment run
    /// </summary>
    /// <param name="assessmentId">The ID of the assessment</param>
    /// <param name="run">The AssessmentRun Object</param>
    /// <returns></returns>
    [HttpPost]
    [Route("{assessmentId}/runs")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Assessment))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(string))]
    public ActionResult<AssessmentRun> CreateAssessmentRun(int assessmentId, [FromBody] AssessmentRunDto run)
    {

        try
        {
            Logger.Debug("Searching assessment with id {id}", assessmentId);
            var assessmentRuns = _assessmentsService.GetRuns(assessmentId);
            if (assessmentRuns == null)
            {
                Logger.Error("Assessment with id {id} not found", assessmentId);
                return NotFound("Assessment not found");
            }

            var result = _assessmentsService.CreateRun(run);
            
            return result;

        }catch(Exception ex)
        {
            Logger.Error(ex, "Error finding assessment");
            return StatusCode(500, "Error finding assessment");
        }

    }
    
    [HttpPut]
    [Route("{assessmentId}/runs/{runId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Assessment))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(string))]
    public ActionResult<string> UpdateAssessmentRun(int assessmentId, int runId, [FromBody] AssessmentRunDto run)
    {
        var user = GetUser();
        try
        {
            Logger.Debug("Searching assessment with id {id}", assessmentId);
            var assessmentRuns = _assessmentsService.GetRuns(assessmentId);
            if (assessmentRuns == null)
            {
                Logger.Error("Assessment with id {id} not found", assessmentId);
                return NotFound("Assessment not found");
            }

            var dbRun = assessmentRuns.FirstOrDefault(r => r.Id == runId);

            if (dbRun == null)
            {
                Logger.Error("Assessment Run with id {id} not found", runId);
                return NotFound("Assessment run not found");
            }

            run.AnalystId = user.Value;
            
            _assessmentsService.UpdateRun(run);

            return Ok("Run updated");

            //return result;

        }catch(Exception ex)
        {
            Logger.Error(ex, "Error updating assessment run");
            return StatusCode(500, "Error updating assessment run");
        }

    }
    
    [HttpDelete]
    [Route("{assessmentId}/runs/{runId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Assessment))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(string))]
    public ActionResult DeleteAssessmentRun(int assessmentId, int runId)
    {

        try
        {
            Logger.Debug("Deleting assessment run with id {id}", runId);
            
            var assessmentRuns = _assessmentsService.GetRun(runId);
            if (assessmentRuns == null)
            {
                Logger.Error("Assessment run with id {id} not found", assessmentId);
                return NotFound("Assessment not found");
            }

            _assessmentsService.DeleteRun(runId);

            return Ok("Deleted ok");


        }catch(Exception ex)
        {
            Logger.Error(ex, "Error Deleting assessment run");
            return StatusCode(500, "Error Deleting assessment run");
        }

    }
    
    [HttpGet]
    [Route("{assessmentId}/Runs/{runId}/answers")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Assessment))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(string))]
    public ActionResult<List<AssessmentRunsAnswer>?> GetAssessmentRunsQuestions(int assessmentId, int runId)
    {

        try
        {
            
            var assessmentRuns = _assessmentsService.GetRunsAnswers(runId);
            
            return assessmentRuns;

        }catch(Exception ex)
        {
            Logger.Error(ex, "Error finding assessment run answers");
            return StatusCode(500, "Error finding assessment run answers");
        }

    }
    
    [HttpDelete]
    [Route("{assessmentId}/Runs/{runId}/answers")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Assessment))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(string))]
    public ActionResult<string> DeleteAllRunAnswers(int assessmentId, int runId)
    {

        try
        {
            var assessmentRuns = _assessmentsService.GetRun(runId);
            if (assessmentRuns == null)
            {
                Logger.Error("Assessment run with id {id} not found", assessmentId);
                return NotFound("Assessment not found");
            }
            
            _assessmentsService.DeleteAllRunAnswer(assessmentId, runId);
            
            return Ok("Deleted");

        }catch(Exception ex)
        {
            Logger.Error(ex, "Error deleting assessment run answers");
            return StatusCode(500, "Error deleting assessment run answers");
        }

    }
    
    [HttpPost]
    [Route("{assessmentId}/Runs/{runId}/answers")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Assessment))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(string))]
    public ActionResult<AssessmentRunsAnswer?> CreateAssessmentRunsQuestion(int assessmentId, int runId, [FromBody] AssessmentRunsAnswerDto answer)
    {

        try
        {
            var ara = new AssessmentRunsAnswer()
            {
                AnswerId = answer.AnswerId,
                QuestionId = answer.QuestionId,
                RunId = answer.RunId
            };
            
        
            
            var assessmentRunsAnswer = _assessmentsService.CreateRunAnswer(ara);
            
            return assessmentRunsAnswer;

        }catch(Exception ex)
        {
            Logger.Error(ex, "Error creating assessment run answers");
            return StatusCode(500, "Error creating assessment run answers");
        }

    }
    
    [HttpDelete]
    [Route("{assessmentId}/Runs/{runId}/answers/{runAnswerId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Assessment))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(string))]
    public ActionResult<string> CreateAssessmentRunsQuestion(int assessmentId, int runId, int runAnswerId)
    {

        try
        {
            
            _assessmentsService.DeleteRunAnswer(assessmentId, runId, runAnswerId);
            
            return Ok("Deleted ok");

        }catch(Exception ex)
        {
            Logger.Error(ex, "Error deleting assessment run answers");
            return StatusCode(500, "Error deleting assessment run answers");
        }

    }
    
    /// <summary>
    /// Deletes the assessment
    /// </summary>
    /// <param name="assessmentId">If of the assessment</param>
    /// <retf:urns></returns>
    [HttpDelete]
    [Route("{assessmentId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult DeleteAssessment(int assessmentId)
    {

        try
        {
            Logger.Debug("Searching assessment with id {id}", assessmentId);
            var assessment = _assessmentsService.Get(assessmentId);
            if (assessment == null)
            {
                Logger.Error("Assessment with id {id} not found", assessmentId);
                return NotFound("Assessment not found");
            }
            
            var result = _assessmentsService.Delete(assessment);
            
            if(result == 0)
            {
                Logger.Information("Assessment with id {id} deleted", assessmentId);
                return Ok();
            }
            else
            {
                Logger.Error("Error deleting assessment with id {id}", assessmentId);
                return StatusCode(500, "Error deleting assessment");
            }

        }catch(Exception ex)
        {
            Logger.Error(ex, "Error finding assessment");
            return StatusCode(500, "Error finding assessment");
        }

    }
    
    /// <summary>
    /// Creates an assessment
    /// </summary>
    /// <param name="assessment">The assessment object</param>
    /// <returns></returns>
    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(Assessment))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(string))]
    public ActionResult<Assessment> CreateAssessment([FromBody] Assessment assessment)
    {

        try
        {
            Logger.Debug("Creating new assessment");
            var operResult = _assessmentsService.Create(assessment);
            if (operResult.Item2 is null)
            {
                return StatusCode(500, "Error creating assessment");
            }
            
            if (operResult.Item1 == 1)
            {
                return Conflict("Assessment already exists");
            }

            if (operResult.Item1 == 0)
            {
                return Created($"/Assessments/{operResult.Item2.Id}", operResult.Item2);
                //return CreatedAtAction(nameof(GetAssessment), new { id = assessment.Id }, assessment);
            }
            
            return StatusCode(500, "Error creating assessment");
            

        }catch(Exception ex)
        {
            Logger.Error("Error creating assessment: {0}", ex.Message);
            return StatusCode(500, "Error creating assessment");
        }

    }
    
    /// <summary>
    /// Updates an assessment
    /// </summary>
    /// <param name="assessment">The assessment object</param>
    /// <returns></returns>
    [HttpPut]
    [Route("{assessmentId}")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(Assessment))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(string))]
    public ActionResult<string> UpdateAssessment(int assessmentId, [FromBody] Assessment assessment)
    {

        try
        {
            Logger.Debug("Updating assessment");
            
            if(assessmentId != assessment.Id) assessment.Id = assessmentId;
            
            _assessmentsService.Update(assessment);
            
            return Ok("Assessment Updated");
            

        }catch(Exception ex)
        {
            Logger.Error("Error Updating assessment: {0}", ex.Message);
            return StatusCode(500, "Error Updating assessment");
        }

    }
    
    /// <summary>
    /// Gets all the answers for this assessment
    /// </summary>
    /// <param name="assessmentId">the Id of the assessment</param>
    /// <returns></returns>
    [HttpGet]
    [Route("{assessmentId}/answers")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<AssessmentAnswer>))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(string))]
    public ActionResult<List<AssessmentAnswer>> ListAssessmentAnswers(int assessmentId)
    {

        try
        {
            Logger.Debug("Searching answers for assessment with id {id}", assessmentId);
            var assessmentAnswers = _assessmentsService.GetAnswers(assessmentId);
            if (assessmentAnswers == null)
            {
                Logger.Error("Answers for assessment with id {id} not found", assessmentId);
                return NotFound("Assessment not found");
            }
            
            return assessmentAnswers;

        }catch(Exception ex)
        {
            Logger.Error(ex, "Error finding assessment answers");
            return StatusCode(500, "Answers not found");
        }

    }
    
    [HttpGet]
    [Route("{assessmentId}/questions")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<AssessmentQuestion>))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(string))]
    public ActionResult<List<AssessmentQuestion>> ListAssessmentQuestions(int assessmentId)
    {

        try
        {
            // First we check if the assessment exists
            Logger.Debug("Searching for assessment with id {assessmentId}", assessmentId);
            var assessment = _assessmentsService.Get(assessmentId);
            if (assessment == null)
            {
                Logger.Error("Assessment with id {assessmentId} not found", assessmentId);
                return NotFound("Assessment not found");
            }
            
            Logger.Debug("Searching questions for assessment with id {id}", assessmentId);
            var assessmentQuestions = _assessmentsService.GetQuestions(assessmentId);
            if (assessmentQuestions == null)
            {
                Logger.Error("Questions for assessment with id {id} not found", assessmentId);
                return NotFound("Questions not found");
            }
            
            return assessmentQuestions;

        }catch(Exception ex)
        {
            Logger.Error(ex, "Error finding assessment questions");
            return StatusCode(500, "Questions not found");
        }

    }

    /// <summary>
    /// Creates a new assessment question
    /// </summary>
    /// <param name="assessmentId">The id of the parent assessment</param>
    /// <param name="question">The question to be created</param>
    /// <returns></returns>
    [HttpPost]
    [Route("{assessmentId}/questions")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(AssessmentQuestion))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(string))]
    public ActionResult<AssessmentQuestion> CreateAssessmentQuestion(int assessmentId, [FromBody] AssessmentQuestionDto question)
    {

        try
        {
            if (question.Id != 0) question.Id = 0;
            
            // First we check if the assessment exists
            Logger.Debug("Searching for assessment with id {assessmentId}", assessmentId);
            var assessment = _assessmentsService.Get(assessmentId);
            if (assessment == null)
            {
                Logger.Error("Assessment with id {assessmentId} not found", assessmentId);
                return NotFound("Assessment not found");
            }
            
            if (question.Question == null) return BadRequest("Question cannot be null");
            
            // Now we know it exists let´s check if the question we are tring to create already exists
            Logger.Debug("Searching for question with text {0}", question.Question);
            
            var dbQuestion = _assessmentsService.GetQuestion(assessmentId, question.Question);

            if (dbQuestion is not null)
            {
                Logger.Error("A question with the same text already exists for {assessmentId}", assessmentId);
                return Conflict("Question already exists");
            }

            var result = _assessmentsService.SaveQuestion(question);
            
            if(result == null) return StatusCode(500, "Error creating question");
            
            Logger.Information("Creating question id: {0} for assessment: {1}", result.Id, assessmentId);
            return Created($"/Assessments/{assessmentId}/Questions/{result.Id}", result);

        }catch(Exception ex)
        {
            Logger.Error(ex, "Error finding assessment questions");
            return StatusCode(500, "Questions not found");
        }

    }

    /// <summary>
    /// Updates a assessment question
    /// </summary>
    /// <param name="assessmentId">The id of the parent assessment</param>
    /// <param name="question">The question to be created</param>
    /// <returns></returns>
    [HttpPut]
    [Route("{assessmentId}/questions")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AssessmentQuestion))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(string))]
    public ActionResult<AssessmentQuestion> UpdateAssessmentQuestion(int assessmentId, [FromBody] AssessmentQuestionDto question)
    {

        try
        {
            // First we check if the assessment exists
            Logger.Debug("Searching for assessment with id {assessmentId}", assessmentId);
            var assessment = _assessmentsService.Get(assessmentId);
            if (assessment == null)
            {
                Logger.Error("Assessment with id {assessmentId} not found", assessmentId);
                return NotFound("Assessment not found");
            }
            
            if (question.Id == 0)
            {
                Logger.Error("This is a new question and cannot be updated");
                return Conflict("Trying to update new question");
            }
            
            if (question.AssessmentId != assessmentId)
            {
                Logger.Error("This question does not belong to the apointed assessment");
                return Conflict("Trying to update wrong question");
            }
            
            // Now we know it exists let´s check if the question we are tring to create already exists
            Logger.Debug("Searching for question with id {0}", question.Id);
            
            var dbQuestion = _assessmentsService.GetQuestionById(question.Id);

            if (dbQuestion is  null)
            {
                Logger.Error("The question pointed does not exists on the database");
                return NotFound("Question not found");
            }

            var questionEnt = new AssessmentQuestion();
            question.Adapt(questionEnt);
            var result = _assessmentsService.SaveQuestion(questionEnt);
            
            if(result == null) return StatusCode(500, "Error updating question");
            
            Logger.Information("Updating question id: {0} for assessment: {1}", result.Id, assessmentId);
            return Ok(result);

        }catch(Exception ex)
        {
            Logger.Error(ex, "Error finding assessment questions");
            return StatusCode(500, "Questions not found");
        }

    }
    
    
    /// <summary>
    /// Creates a new assessment question answers
    /// </summary>
    /// <param name="assessmentId">The id of the parent assessment</param>
    /// <param name="questionId">The id of the parent assessment question</param>
    /// <param name="answers">The answers to be created</param>
    /// <returns></returns>
    [HttpPost]
    [Route("{assessmentId}/questions/{questionId}/answers")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(List<AssessmentAnswer>))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(string))]
    public ActionResult<List<AssessmentQuestion>> CreateAssessmentAnswers(int assessmentId, int questionId,
        [FromBody] AssessmentAnswerDto[] answers)
    {

        try
        {
            // First we check if the assessment exists
            Logger.Debug("Searching for assessment with id {assessmentId}", assessmentId);
            var assessment = _assessmentsService.Get(assessmentId);
            if (assessment == null)
            {
                Logger.Error("Assessment with id {assessmentId} not found", assessmentId);
                return NotFound("Assessment not found");
            }
            
            //Now let's check if the question exists 
            var question = _assessmentsService.GetQuestionById(assessmentId, questionId);
            if (question == null)
            {
                Logger.Error("Assessment question with id {questionId} for and assessment {assessmentId} not found",questionId,assessmentId);
                return NotFound("Question not found");
            }
            
            //Checking if all of the answers belongs to the assessment question
            foreach (var answer in answers)
            {
                if (answer.AssessmentId != assessmentId || answer.QuestionId != questionId)
                {
                    Logger.Error("Trying to save inconsistent answer",questionId,assessmentId);
                    return Conflict("Trying to save inconsistent answer");
                }

            }

            //Checking if all of the answers are new
            foreach (var answer in answers)
            {
                // First let's check if it already exists
                var dbAnswer = _assessmentsService.GetAnswer(assessmentId, questionId, answer.Answer);

                if (dbAnswer is not null)
                {
                    Logger.Error("A answer with the same text already exists for {assessmentId} & {questionId}", assessmentId, questionId);
                    return Conflict("One of the answers already exists");
                }
            }

            var result = new List<AssessmentAnswer>();
            //Saving the answers
            foreach (var answer in answers)
            {
                var resAnswer = _assessmentsService.SaveAnswer(answer);
                result.Add(resAnswer!);
            }

            return Created($"{assessmentId}/questions/{questionId}/answers",result);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error creating assessment answers");
            return StatusCode(500, "Error creating assessment answers");
        }
        
        
    }
    
    /// <summary>
    /// Updates some assessment question answers
    /// </summary>
    /// <param name="assessmentId">The id of the parent assessment</param>
    /// <param name="questionId">The id of the parent assessment question</param>
    /// <param name="answers">The answers to be created</param>
    /// <returns></returns>
    [HttpPut]
    [Route("{assessmentId}/questions/{questionId}/answers")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<AssessmentAnswer>))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(string))]
    public ActionResult<List<AssessmentQuestion>> UpdateAssessmentAnswers(int assessmentId, int questionId,
        [FromBody] AssessmentAnswerDto[] answers)
    {

        try
        {
            // First we check if the assessment exists
            Logger.Debug("Searching for assessment with id {assessmentId}", assessmentId);
            var assessment = _assessmentsService.Get(assessmentId);
            if (assessment == null)
            {
                Logger.Error("Assessment with id {assessmentId} not found", assessmentId);
                return NotFound("Assessment not found");
            }
            
            //Now let's check if the question exists 
            var question = _assessmentsService.GetQuestionById(assessmentId, questionId);
            if (question == null)
            {
                Logger.Error("Assessment question with id {questionId} for and assessment {assessmentId} not found",questionId,assessmentId);
                return NotFound("Question not found");
            }
            
            //Checking if all of the answers belongs to the assessment question
            foreach (var answer in answers)
            {
                if (answer.AssessmentId != assessmentId || answer.QuestionId != questionId)
                {
                    Logger.Error("Trying to save inconsistent answer",questionId,assessmentId);
                    return Conflict("Trying to save inconsistent answer");
                }

            }

            //Checking if all of the answers already exists
            foreach (var answer in answers)
            {
                // First let's check if it already exists
                var dbAnswer = _assessmentsService.GetAnswerById(answer.Id);

                if (dbAnswer is null)
                {
                    Logger.Error("A answer with the same text already exists for {assessmentId} & {questionId}", assessmentId, questionId);
                    return Conflict("One of the answers already exists");
                }
            }

            var result = new List<AssessmentAnswer>();
            //Saving the answers
            foreach (var answer in answers)
            {
                var resAnswer = _assessmentsService.SaveAnswer(answer);
                result.Add(resAnswer!);
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error creating assessment answers");
            return StatusCode(500, "Error creating assessment answers");
        }
        
        
    }
    
    [HttpDelete]
    [Route("{assessmentId}/questions/{questionId}/answers/{answerId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(string))]
    public ActionResult<string> DeleteAssessmentAnswer(int assessmentId, int questionId,
        int answerId)
    {

        try
        {
            // First we check if the assessment exists
            Logger.Debug("Searching for assessment with id {assessmentId}", assessmentId);
            var assessment = _assessmentsService.Get(assessmentId);
            if (assessment == null)
            {
                Logger.Error("Assessment with id {assessmentId} not found", assessmentId);
                return NotFound("Assessment not found");
            }
            
            //Now let's check if the question exists 
            var question = _assessmentsService.GetQuestionById(assessmentId, questionId);
            if (question == null)
            {
                Logger.Error("Assessment question with id {questionId} for and assessment {assessmentId} not found",questionId,assessmentId);
                return NotFound("Question not found");
            }
            
            //Now let's find the answer 
            var dbAnswer = _assessmentsService.GetAnswerById(answerId);

            if (dbAnswer is null)
            {
                Logger.Error("A answer could not be found for deletion {answerId}", answerId);
                return NotFound("Answer not found");
            }
            
            var resAnswer = _assessmentsService.DeleteAnswer(dbAnswer);
            
            var result = resAnswer == 0 ? "Ok" : "Error";
            return Ok(result);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error deleting assessment answer");
            return StatusCode(500, "Error deleting assessment answer");
        }
        
        
    }
    
    [HttpDelete]
    [Route("{assessmentId}/questions/{questionId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(string))]
    public ActionResult<string> DeleteAssessmentQuestion(int assessmentId, int questionId)
    {

        try
        {
            // First we check if the assessment exists
            Logger.Debug("Searching for assessment with id {assessmentId}", assessmentId);
            var assessment = _assessmentsService.Get(assessmentId);
            if (assessment == null)
            {
                Logger.Error("Assessment with id {assessmentId} not found", assessmentId);
                return NotFound("Assessment not found");
            }
            
            //Now let's check if the question exists 
            var question = _assessmentsService.GetQuestionById(assessmentId, questionId);
            if (question == null)
            {
                Logger.Error("Assessment question with id {questionId} for and assessment {assessmentId} not found",questionId,assessmentId);
                return NotFound("Question not found");
            }
            
            
            var resAnswer = _assessmentsService.DeleteQuestion(question);
            
            var result = resAnswer == 0 ? "Ok" : "Error";
            return Ok(result);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error deleting assessment question");
            return StatusCode(500, "Error deleting assessment question");
        }
        
        
    }
}