using Mapster;
using System.Text.Json;
using DAL;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Assessments;
using Model.DTO;
using Model.Exceptions;
using ServerServices.Interfaces;

namespace ServerServices.Services;
using ILogger = Serilog.ILogger;


public class AssessmentsService: ServiceBase, IAssessmentsService
{
    public AssessmentsService(ILogger logger, IDalService dalService): base(logger, dalService)
    {
    }
    
    public List<Assessment> List()
    {
        var srDbContext = DalService.GetContext();
        
        return srDbContext.Assessments.ToList();
        
    }

    public Assessment? Get(int id)
    {
        using var srDbContext = DalService.GetContext();
        return srDbContext.Assessments.Find(id);
    }

    public List<AssessmentRun> GetRuns(int id)
    {
        try
        {
            using var dbContext = DalService.GetContext(); 
            
            // first let's check if the assessment exists 
            var assessment = dbContext.Assessments.FirstOrDefault(a => a.Id == id);
            if (assessment is null)
            {
                throw new DataNotFoundException("assessment", id.ToString());
            }
            
            var runs = dbContext.AssessmentRuns.Where(r => r.AssessmentId == id)
                .Include(r=>r.Entity)
                .ThenInclude( e => e.EntitiesProperties)
                .ToList();

            return runs;

        }catch(Exception ex)
        {
            Logger.Error("Error getting assessment runs: {0}", ex.Message);
            throw new DataNotFoundException("assessment", id.ToString(), ex);
        }
        
    }

    public AssessmentRun CreateRun(AssessmentRunDto run)
    {
        run.Id = 0;

        try
        {
            using var dbContext = DalService.GetContext();



            var arun = new AssessmentRun()
            {
                Id = run.Id,
                AssessmentId = run.AssessmentId,
                AnalystId = run.AnalystId,
                EntityId = run.EntityId,
                HostId = run.HostId,
                Status = (int)AssessmentStatus.Open,
                RunDate = run.RunDate, 
                Comments = run.Comments
                
            };

            return CreateRun(arun);

        }catch(Exception ex)
        {
            Logger.Error("Error creating assessment run: {0}", ex.Message);
            throw;
        }
    }
    
    public AssessmentRun CreateRun(AssessmentRun run)
    {
        run.Id = 0;

        try
        {
            using var dbContext = DalService.GetContext();
            
            // check if assessment exists
            var assessment = dbContext.Assessments.Find(run.AssessmentId);
            if( assessment == null) throw new DataNotFoundException("assessment", run.AssessmentId.ToString());
            
            // check if entity exists
            var entity = dbContext.Entities.Find(run.EntityId);
            if(entity == null) throw new DataNotFoundException("entity", run.EntityId.ToString());
            
            // check if user exists
            var analyst = dbContext.Users.Find(run.AnalystId);
            if( analyst == null) throw new DataNotFoundException("user", run.AnalystId!.ToString()!);
            
            var result = dbContext.AssessmentRuns.Add(run);
            dbContext.SaveChanges();

            result.Entity.Analyst = null;
            
            return result.Entity;

        }catch(Exception ex)
        {
            Logger.Error("Error creating assessment run: {0}", ex.Message);
            throw;
        }

        
    }

    public void UpdateRun(AssessmentRunDto run)
    {
        try
        {
            using var dbContext = DalService.GetContext();
            
            // check if assessment exists
            var assessment = dbContext.Assessments.Find(run.AssessmentId);
            if( assessment == null) throw new DataNotFoundException("assessment", run.AssessmentId.ToString());
            
            // check if entity exists
            var entity = dbContext.Entities.Find(run.EntityId);
            if(entity == null) throw new DataNotFoundException("entity", run.EntityId.ToString());
            
            // check if user exists
            var analyst = dbContext.Users.Find(run.AnalystId);
            if( analyst == null) throw new DataNotFoundException("user", run.AnalystId!.ToString()!);
            
            // A run targets an entity, and optionally one of its hosts (M58, T280): a run without a host is saved, not
            // refused as a host "not found".
            Host? host = null;
            if (run.HostId is { } hostId)
            {
                host = dbContext.Hosts.Find(hostId);
                if (host == null) throw new DataNotFoundException("host", hostId.ToString());
            }
            
            //check if run exists 
            var dbRun = dbContext.AssessmentRuns.Find(run.Id);
            if(dbRun == null) throw new DataNotFoundException("run", run.Id.ToString());

            dbRun.Entity = entity;
            dbRun.Analyst = analyst;
            dbRun.Comments = run.Comments;
            dbRun.Host = host;
            dbRun.HostId = host?.Id;

            // M58, T280 (S39 D-16): RunDate is the date the run was created with, no longer "the last save". A status the
            // caller leaves out keeps the run's, so a save that only edits a comment cannot reopen a submitted run.
            var wasSubmitted = dbRun.Status == (int)AssessmentStatus.Submitted;
            if (run.Status != null) dbRun.Status = (int)run.Status;
            var isSubmitted = dbRun.Status == (int)AssessmentStatus.Submitted;

            if (isSubmitted && !wasSubmitted)
            {
                dbRun.SubmittedAt = DateTime.UtcNow;
                dbRun.Score = ScoreRun(dbContext, dbRun);
            }
            else if (!isSubmitted)
            {
                dbRun.SubmittedAt = null;
                dbRun.Score = null;
            }
            
            dbContext.SaveChanges();
            

        }catch(Exception ex)
        {
            Logger.Error("Error creating assessment run: {0}", ex.Message);
            throw;
        }
    }

    public AssessmentRunsAnswer CreateRunAnswer(AssessmentRunsAnswer answer)
    {
        answer.Id = 0;
        try
        {
            using var dbContext = DalService.GetContext();
            
            // check if run exists
            var run = dbContext.AssessmentRuns.Find(answer.RunId);
            if( run == null) throw new DataNotFoundException("AssessmentRuns", answer.RunId.ToString());
            
            // check if answer exists
            var baseAnswer = dbContext.AssessmentAnswers.Find(answer.AnswerId);
            if(baseAnswer == null) throw new DataNotFoundException("AssessmentAnswers", answer.AnswerId.ToString());
            
            // check if question exists
            var question = dbContext.AssessmentQuestions.Find(answer.QuestionId);
            if( question == null) throw new DataNotFoundException("AssessmentQuestions", answer.QuestionId.ToString());
            

            var result = dbContext.AssessmentRunsAnswers.Add(answer);
            dbContext.SaveChanges();

            return result.Entity;

        }catch(Exception ex)
        {
            Logger.Error("Error creating assessment run answer: {0}", ex.Message);
            throw;
        }
    }

    public  void DeleteAllRunAnswer(int assessmentId, int runId)
    {
        try
        {
            using var dbContext = DalService.GetContext();
            
            // check if assessment exists
            var assessment = dbContext.Assessments.Find(assessmentId);
            if( assessment == null) throw new DataNotFoundException("assessment", assessmentId.ToString());
            
            // check if run exists
            var run = dbContext.AssessmentRuns.Include(a => a.AssessmentRunsAnswers).FirstOrDefault(ar => ar.Id == runId);
            if( run == null) throw new DataNotFoundException("AssessmentRuns", runId.ToString());

            run.AssessmentRunsAnswers.Clear();
            
            dbContext.SaveChanges();


        }catch(Exception ex)
        {
            Logger.Error("Error creating assessment run answer: {0}", ex.Message);
            throw;
        }
    }

    public void DeleteRunAnswer(int assessmentId, int runId, int runAnswerId)
    {
        try
        {
            using var dbContext = DalService.GetContext();
            
            // check if run exists
            var run = dbContext.AssessmentRuns.Find(runId);
                if( run == null) throw new DataNotFoundException("AssessmentRuns", runId.ToString());
            
            // check if answer exists
            var runAnswer = dbContext.AssessmentRunsAnswers.Find(runAnswerId);
            if(runAnswer == null) throw new DataNotFoundException("AssessmentRunsAnswers", runAnswerId.ToString());
            
            // check if assessment exists
            var assessment = dbContext.Assessments.Find(assessmentId);
            if( assessment == null) throw new DataNotFoundException("assessment", assessmentId.ToString());
            
            // check if answer belongs to the run 
            if(runAnswer.RunId != runId) throw new RuleBrokenException("Answer does not belong to the run");


            dbContext.AssessmentRunsAnswers.Remove(runAnswer);
            dbContext.SaveChanges();


        }catch(Exception ex)
        {
            Logger.Error("Error creating assessment run answer: {0}", ex.Message);
            throw;
        }
    }
    
    public AssessmentRun? GetRun(int id)
    {
        try
        {
            using var dbContext = DalService.GetContext();
            
            // first let's check if the assessment exists 
            var run = dbContext.AssessmentRuns.Where(r => r.Id == id)
                .Include(r=>r.Entity)
                .ThenInclude( e => e.EntitiesProperties)
                .FirstOrDefault();

            return run;

        }catch(Exception ex)
        {
            Logger.Error("Error getting assessment run: {0}", ex.Message);
            throw new DataNotFoundException("assessment", id.ToString(), ex);
        }
    }

    public void DeleteRun(int id)
    {
        try
        {
            using var dbContext = DalService.GetContext();
            
            var run = dbContext.AssessmentRuns.Find(id);
            if(run == null) throw new DataNotFoundException("AssessmentRuns", id.ToString());
            
            dbContext.AssessmentRuns.Remove(run);
            dbContext.SaveChanges();
            
        }
        catch (Exception e)
        {
            Logger.Error("Error deleting assessment run: {0}", e.Message);
            throw;
        } 
    }
    
    public List<AssessmentRunsAnswer>? GetRunsAnswers(int runnId)
    {
        try
        {
            using var srDbContext = DalService.GetContext(); 
            
            // first let's check if the assessment exists 
            var answers = srDbContext
                .AssessmentRunsAnswers
                .Include(a => a.Answer)
                .Include(a => a.Question)
                .Where(a => a.RunId == runnId).ToList();

            return answers;

        }catch(Exception ex)
        {
            Logger.Error("Error getting assessment runs awnswers: {0}", ex.Message);
            throw;
        } 
    }

    public int Delete(Assessment assessment)
    {
        using var srDbContext = DalService.GetContext();
        //Before we can delete an assessment we need to delete it's questions

        var assQuestions = GetQuestions(assessment.Id);

        if (assQuestions is null)
        {
            Logger.Error("Error loading assessment questions");
            return -1;
        }

        foreach (var question in assQuestions)
        {
            var qdel = DeleteQuestion(question);
            if (qdel != 0) return 1;
        }
        
        
        var result= srDbContext.Assessments.Remove(assessment);
        srDbContext.SaveChanges();
        if(result.State == EntityState.Detached)
            return 0;
        return -1;
    }
    
    public Tuple<int,Assessment?> Create(Assessment assessment)
    {
        using var dbContext = DalService.GetContext();
        try
        {
            var ass = dbContext.Assessments.Add(assessment);
            dbContext.SaveChanges();
            if (ass.IsKeySet)
            {
                return new Tuple<int, Assessment?>(0, ass.Entity);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Error creating assessment: {0}", ex.Message);
            return new Tuple<int, Assessment?>(1, null);
        }
        Logger.Error("Unkown error creating assessment");
        return new Tuple<int, Assessment?>(-1, null);
    }

    public void Update(Assessment assessment)
    {
        using var dbContext = DalService.GetContext();
        try
        {
            var dbAssessment = dbContext.Assessments.FirstOrDefault(a => a.Id == assessment.Id);
            if (dbAssessment == null) throw new DataNotFoundException("assessment", assessment.Id.ToString());

            assessment.Created = dbAssessment.Created;
            
            assessment.Adapt(dbAssessment);
            dbContext.SaveChanges();
            return;

        }
        catch (Exception ex)
        {
            Logger.Error("Error updating assessment: {Message}", ex.Message);
            throw new DatabaseException($"Error updating assessment: {ex.Message}");

        }
    }
    
    public List<AssessmentAnswer>? GetAnswers(int id)
    {
        using var srDbContext = DalService.GetContext();
        return srDbContext.AssessmentAnswers.Where(a => a.AssessmentId == id).ToList();
    }
    
    public List<AssessmentAnswer>? GetQuestionAnswers(int questionId)
    {
        using var srDbContext = DalService.GetContext();
        return srDbContext.AssessmentAnswers.Where(a => a.QuestionId == questionId).ToList();
    }
    
    public AssessmentAnswer? GetAnswer(int assessmentId, int questionId, string answerText)
    {
        using var srDbContext = DalService.GetContext();
        return srDbContext.AssessmentAnswers.FirstOrDefault(a => a.AssessmentId == assessmentId
                                                                 && a.QuestionId == questionId
                                                                 && a.Answer == answerText);
    }

    public AssessmentAnswer? GetAnswerById(int answerId)
    {
        using var srDbContext = DalService.GetContext();
        return srDbContext.AssessmentAnswers.FirstOrDefault(a => a.Id == answerId);
    }
    
    public List<AssessmentQuestion>? GetQuestions(int id)
    {
        using var srDbContext = DalService.GetContext();
        return srDbContext.AssessmentQuestions.Include(aq => aq.AssessmentAnswers)
            .Where(a => a.AssessmentId == id).ToList();
    }

    public AssessmentQuestion? GetQuestion(int id, string question)
    {
        using var srDbContext = DalService.GetContext();
        return srDbContext.AssessmentQuestions.FirstOrDefault(a => a.AssessmentId == id && a.Question == question);
    }

    public AssessmentQuestion? GetQuestionById(int questionId)
    {
        using var srDbContext = DalService.GetContext();
        return srDbContext.AssessmentQuestions.FirstOrDefault(a => a.Id == questionId);
    }
    
    public AssessmentQuestion? GetQuestionById(int id, int questionId)
    {
        using var srDbContext = DalService.GetContext();
        return srDbContext.AssessmentQuestions.FirstOrDefault(a => a.AssessmentId == id && a.Id == questionId);
    }
    
    public AssessmentQuestion? SaveQuestion(AssessmentQuestion question)
    {
        var questionR = new AssessmentQuestion();
        using var srDbContext = DalService.GetContext();
        if (srDbContext.Assessments.FirstOrDefault(ass => ass.Id == question.AssessmentId) is null)
        {
            throw new InvalidReferenceException($"The assessment {question.AssessmentId} indicated on the question does not exists");
        }

        if (question.Id == 0)
        {
            question.Adapt(questionR);
            questionR.Question = question.Question;
            srDbContext.AssessmentQuestions.Add(questionR);
        }
        else
        {
            questionR = srDbContext.AssessmentQuestions.Find(question.Id);

            if (questionR == null) throw new DataNotFoundException("question","Question not found");

            //question.Adapt(questionR);

            questionR.Question = question.Question;
            

            //srDbContext.AssessmentQuestions.Update(questionR);
        }
        srDbContext.SaveChanges();
        return questionR;
    }
    
    public AssessmentAnswer? SaveAnswer(AssessmentAnswer answer)
    {
        using var srDbContext = DalService.GetContext();
        if (srDbContext.Assessments.FirstOrDefault(ass => ass.Id == answer.AssessmentId) is null)
        {
            throw new InvalidReferenceException($"The assessment {answer.AssessmentId} indicated on the answer does not exists");
        }
        if (srDbContext.AssessmentQuestions.FirstOrDefault(ass => ass.Id == answer.QuestionId) is null)
        {
            throw new InvalidReferenceException($"The question {answer.QuestionId} indicated on the answer does not exists");
        }

        if (answer.Id == 0) srDbContext.AssessmentAnswers.Add(answer);
        else srDbContext.AssessmentAnswers.Update(answer);
        srDbContext.SaveChanges();
        return answer;
    }

    public int DeleteAnswer(AssessmentAnswer answer)
    {
        try
        {
            using var srDbContext = DalService.GetContext();
            var ent = srDbContext.AssessmentAnswers.FirstOrDefault(ass => ass.Id == answer.Id);
            if ( ent is null)
            {
                throw new InvalidReferenceException($"The answer {answer.Id} does not exists");
            }

            srDbContext.AssessmentAnswers.Remove(ent);
            srDbContext.SaveChanges();
            return 0;
        }
        catch (Exception ex)
        {
            Logger.Error("Unkown error: {0}", ex.Message);
            return -1;
        }

    }
    
    public int DeleteQuestion(AssessmentQuestion question)
    {
        try
        {
            using var srDbContext = DalService.GetContext();
            var ent = srDbContext.AssessmentQuestions.FirstOrDefault(ass => ass.Id == question.Id);
            if ( ent is null)
            {
                throw new InvalidReferenceException($"The answer {question.Id} does not exists");
            }
            
            //Before deleting a question we need to delete all the answers it has
            var answers = GetQuestionAnswers(question.Id);

            if (answers is null)
            {
                Logger.Error("Error loading asnswers from database");
                return -1;
            }
            
            foreach (var answer in answers)
            {
                var delAnsrRes = DeleteAnswer(answer);
                if (delAnsrRes != 0) return 1;
            }

            srDbContext.AssessmentQuestions.Remove(ent);
            srDbContext.SaveChanges();
            return 0;
        }
        catch (Exception ex)
        {
            Logger.Error("Unkown error: {0}", ex.Message);
            return -1;
        }

    }

    public async Task<AssessmentRunAnswer> SaveDraftAnswerAsync(int runId, int questionId, string answerContentJson,
        int? assessmentAnswerId = null)
    {
        Logger.Information("Saving draft answer for run {RunId}, question {QuestionId}", runId, questionId);
        using var dbContext = DalService.GetContext();

        var run = await dbContext.AssessmentRuns.FirstOrDefaultAsync(r => r.Id == runId)
                  ?? throw new DataNotFoundException("AssessmentRuns", runId.ToString());
        var question = await dbContext.AssessmentQuestions.FirstOrDefaultAsync(q => q.Id == questionId)
                       ?? throw new DataNotFoundException("AssessmentQuestions", questionId.ToString());
        if (question.AssessmentId != run.AssessmentId)
            throw new InvalidParameterException("questionId", $"Question {questionId} is not part of the run's assessment.");

        var optionId = await LinkOptionAsync(dbContext, questionId, answerContentJson, assessmentAnswerId);

        var existingAnswer = await dbContext.AssessmentRunAnswers
            .FirstOrDefaultAsync(a => a.AssessmentRunId == runId && a.AssessmentQuestionId == questionId);

        if (existingAnswer != null)
        {
            existingAnswer.AnswerContentJson = answerContentJson;
            existingAnswer.AssessmentAnswerId = optionId;
            existingAnswer.LastUpdatedAt = DateTime.UtcNow;
            dbContext.AssessmentRunAnswers.Update(existingAnswer);
            await dbContext.SaveChangesAsync();
            return existingAnswer;
        }

        var newAnswer = new AssessmentRunAnswer
        {
            AssessmentRunId = runId,
            AssessmentQuestionId = questionId,
            AnswerContentJson = answerContentJson,
            AssessmentAnswerId = optionId,
            LastUpdatedAt = DateTime.UtcNow
        };

        dbContext.AssessmentRunAnswers.Add(newAnswer);
        await dbContext.SaveChangesAsync();
        return newAnswer;
    }

    public async Task<List<AssessmentRunAnswer>> GetDraftAnswersAsync(int runId)
    {
        using var dbContext = DalService.GetContext();
        return await dbContext.AssessmentRunAnswers
            .Where(a => a.AssessmentRunId == runId)
            .ToListAsync();
    }

    public async Task<List<AssessmentQuestion>> GetVisibleQuestionsForPageAsync(int runId, int pageNumber)
    {
        Logger.Information("Evaluating visible questions for run {RunId}, page {PageNumber}", runId, pageNumber);
        using var dbContext = DalService.GetContext();

        var run = await dbContext.AssessmentRuns.FindAsync(runId);
        if (run == null)
            throw new ArgumentException($"Assessment run {runId} not found", nameof(runId));

        var allPageQuestions = await dbContext.AssessmentQuestions
            .Where(q => q.AssessmentId == run.AssessmentId && q.PageNumber == pageNumber)
            .OrderBy(q => q.Order)
            .ToListAsync();

        var answers = await dbContext.AssessmentRunAnswers.AsNoTracking()
            .Where(a => a.AssessmentRunId == runId)
            .Select(a => new { a.AssessmentQuestionId, a.AnswerContentJson })
            .ToListAsync();
        var contentByQuestion = answers.GroupBy(a => a.AssessmentQuestionId)
            .ToDictionary(g => g.Key, g => g.First().AnswerContentJson);

        return allPageQuestions.Where(q => IsVisible(q, contentByQuestion)).ToList();
    }

    /// <summary>
    /// Whether a question is shown, given the run's stored answer contents by question id: always without a condition,
    /// else by its condition on the referenced answer. A condition that cannot be read shows the question (safe fallback).
    /// The wizard's pages and the submission score (T280) both use this, so the score counts what the assessor saw.
    /// </summary>
    private bool IsVisible(AssessmentQuestion question, IReadOnlyDictionary<int, string?> contentByQuestion)
    {
        if (string.IsNullOrEmpty(question.ConditionJson)) return true;

        try
        {
            var condition = JsonSerializer.Deserialize<QuestionCondition>(question.ConditionJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (condition == null) return true;

            var ansValue = contentByQuestion.GetValueOrDefault(condition.QuestionId)?.Trim('"');

            switch (condition.Operator.ToLowerInvariant())
            {
                case "equals":
                    return ansValue != null && ansValue.Equals(condition.Value, StringComparison.OrdinalIgnoreCase);
                case "notempty":
                    return !string.IsNullOrEmpty(ansValue);
                case "in":
                    if (ansValue == null || condition.Value == null) return false;
                    var allowedValues = condition.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    return allowedValues.Contains(ansValue, StringComparer.OrdinalIgnoreCase);
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error evaluating conditional logic for question {QuestionId}", question.Id);
            return true; // safe fallback
        }
    }

    /// <summary>
    /// M58, T280 (S39 §3.1, D-16): the score of a run being submitted — <see cref="AssessmentRunScore"/> over the questions
    /// visible with its answers, reading each answer's option by id.
    /// </summary>
    private double? ScoreRun(DAL.Context.NRDbContext dbContext, AssessmentRun run)
    {
        var questions = dbContext.AssessmentQuestions.AsNoTracking().Where(q => q.AssessmentId == run.AssessmentId).ToList();
        var options = dbContext.AssessmentAnswers.AsNoTracking().Where(a => a.AssessmentId == run.AssessmentId).ToList();
        var answers = dbContext.AssessmentRunAnswers.AsNoTracking().Where(a => a.AssessmentRunId == run.Id)
            .Select(a => new { a.AssessmentQuestionId, a.AnswerContentJson, a.AssessmentAnswerId })
            .ToList();

        var contentByQuestion = answers.GroupBy(a => a.AssessmentQuestionId)
            .ToDictionary(g => g.Key, g => g.First().AnswerContentJson);
        var chosen = answers.Where(a => a.AssessmentAnswerId != null).GroupBy(a => a.AssessmentQuestionId)
            .ToDictionary(g => g.Key, g => g.First().AssessmentAnswerId!.Value);
        var visible = questions.Where(q => IsVisible(q, contentByQuestion)).Select(q => q.Id);

        return AssessmentRunScore.Compute(visible, chosen, options);
    }

    /// <summary>
    /// The option a draft names (T280): the one the client sent, which must be the question's, else the question's one
    /// option with the content's text; null when the content is empty or the text matches none or several.
    /// </summary>
    private static async Task<int?> LinkOptionAsync(DAL.Context.NRDbContext dbContext, int questionId, string? answerContentJson,
        int? assessmentAnswerId)
    {
        var options = await dbContext.AssessmentAnswers.AsNoTracking().Where(a => a.QuestionId == questionId).ToListAsync();

        if (assessmentAnswerId is { } id)
        {
            if (options.All(o => o.Id != id))
                throw new InvalidParameterException("assessmentAnswerId",
                    $"Option {id} is not one of question {questionId}'s options.");
            return id;
        }

        var text = AssessmentAnswerLink.DecodeText(answerContentJson);
        return text.Length == 0 ? null : AssessmentAnswerLink.MatchByText(options, text).Option?.Id;
    }

    public async Task<List<AssessmentAnswerLinkGap>> GetUnlinkedAnswersAsync()
    {
        using var dbContext = DalService.GetContext();

        var unlinked = await dbContext.AssessmentRunAnswers.AsNoTracking()
            .Where(a => a.AssessmentAnswerId == null && a.AnswerContentJson != null && a.AnswerContentJson != "")
            .Select(a => new { a.Id, a.AssessmentRunId, a.AssessmentQuestionId, a.AnswerContentJson, a.AssessmentRun.AssessmentId })
            .ToListAsync();

        var questionIds = unlinked.Select(a => a.AssessmentQuestionId).Distinct().ToList();
        var options = (await dbContext.AssessmentAnswers.AsNoTracking().Where(o => questionIds.Contains(o.QuestionId)).ToListAsync())
            .ToLookup(o => o.QuestionId);

        var gaps = new List<AssessmentAnswerLinkGap>();
        foreach (var answer in unlinked.OrderBy(a => a.AssessmentRunId).ThenBy(a => a.AssessmentQuestionId))
        {
            var text = AssessmentAnswerLink.DecodeText(answer.AnswerContentJson);
            if (text.Length == 0) continue;

            var (option, reason) = AssessmentAnswerLink.MatchByText(options[answer.AssessmentQuestionId], text);
            gaps.Add(new AssessmentAnswerLinkGap
            {
                RunAnswerId = answer.Id, RunId = answer.AssessmentRunId, AssessmentId = answer.AssessmentId,
                QuestionId = answer.AssessmentQuestionId, AnswerText = text,
                Reason = option is not null ? AssessmentAnswerLinkGap.Matchable
                    : reason == AssessmentAnswerLink.Unmatched.Ambiguous ? AssessmentAnswerLinkGap.Ambiguous
                    : AssessmentAnswerLinkGap.NoMatch
            });
        }

        return gaps;
    }
}

public class QuestionCondition
{
    public int QuestionId { get; set; }
    public string Operator { get; set; } = "equals";
    public string? Value { get; set; }
}