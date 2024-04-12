using AutoMapper;
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
    private IMapper Mapper { get; }
    
    public AssessmentsService(ILogger logger, IMapper mapper, IDalService dalService): base(logger, dalService)
    {
        Mapper = mapper;
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
            
            var host = dbContext.Hosts.Find(run.HostId);
            if(host == null) throw new DataNotFoundException("host", run.HostId!.ToString()!);
            
            //check if run exists 
            var dbRun = dbContext.AssessmentRuns.Find(run.Id);
            if(dbRun == null) throw new DataNotFoundException("run", run.Id.ToString());

            //Mapper.Map(run, dbRun);
            
            //dbRun.EntityId = run.EntityId;
            dbRun.Entity = entity;
            dbRun.Analyst = analyst;
            dbRun.Comments = run.Comments;
            dbRun.Host = host;
            
            dbRun.RunDate = DateTime.Now;
            if(run.Status != null) dbRun.Status = (int) run.Status;
            else dbRun.Status = (int) AssessmentStatus.Open;
            
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
            
            Mapper.Map(assessment, dbAssessment);
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
            Mapper.Map(question, questionR);
            srDbContext.AssessmentQuestions.Add(questionR);
        }
        else
        {
            questionR = srDbContext.AssessmentQuestions.Find(question.Id);

            if (questionR == null) throw new DataNotFoundException("question","Question not found");

            //Mapper.Map(question, questionR);

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
}