using DAL;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using ServerServices.Interfaces;

namespace ServerServices.Services;
using ILogger = Serilog.ILogger;


public class AssessmentsService: ServiceBase, IAssessmentsService
{
    
    public AssessmentsService(ILogger logger, DALService dalService): base(logger, dalService)
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

    public AssessmentRun CreateRun(AssessmentRun run)
    {
        run.Id = 0;

        try
        {
            using var dbContext = DalService.GetContext();
            // check if assessment exists
            if(dbContext.Assessments.Find(run.AssessmentId) == null) throw new DataNotFoundException("assessment", run.AssessmentId.ToString());
            
            // check if entity exists
            if(dbContext.Entities.Find(run.EntityId) == null) throw new DataNotFoundException("entity", run.EntityId.ToString());
            
            // check if user exists
            if(dbContext.Users.Find(run.AnalystId) == null) throw new DataNotFoundException("user", run.AnalystId.ToString());

            var result = dbContext.AssessmentRuns.Add(run);
            dbContext.SaveChanges();
            
            return result.Entity;

        }catch(Exception ex)
        {
            Logger.Error("Error creating assessment run: {0}", ex.Message);
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
        using var srDbContext = DalService.GetContext();
        try
        {
            var ass = srDbContext.Assessments.Add(assessment);
            srDbContext.SaveChanges();
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
        using var srDbContext = DalService.GetContext();
        if (srDbContext.Assessments.FirstOrDefault(ass => ass.Id == question.AssessmentId) is null)
        {
            throw new InvalidReferenceException($"The assessment {question.AssessmentId} indicated on the question does not exists");
        }

        if (question.Id == 0) srDbContext.AssessmentQuestions.Add(question);
        else srDbContext.AssessmentQuestions.Update(question);
        srDbContext.SaveChanges();
        return question;
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
}