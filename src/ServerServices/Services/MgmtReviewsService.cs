using AutoMapper;
using DAL;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.DTO;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class MgmtReviewsService: ServiceBase, IMgmtReviewsService
{
    private readonly IMapper _mapper;
    
    public MgmtReviewsService(
        ILogger logger, 
        DALService dalService,
        IMapper mapper
    ): base(logger, dalService)
    {
        _mapper = mapper;
    }

    private void RiskExists(int riskId)
    {
        using var dbContext = DalService.GetContext();
        // Check if risk exists 
        var risk = dbContext.Risks.FirstOrDefault(r => r.Id == riskId);
        if (risk == null)
            throw new DataNotFoundException("local", "risks", new Exception($"Risk with id {riskId} not found"));
    }

    public List<MgmtReview> GetRiskReviews(int riskId)
    {
        using var dbContext = DalService.GetContext();
        
        RiskExists(riskId);

        var reviews = dbContext.MgmtReviews.Where(mr => mr.RiskId == riskId).ToList(); 

        return reviews;
    }

    public List<Review> GetReviewTypes()
    {
        using var dbContext = DalService.GetContext();

        var reviews = dbContext.Reviews.ToList();

        return reviews; 
    }

    public List<NextStep> GetNextSteps()
    {
        using var dbContext = DalService.GetContext();

        var nextSteps = dbContext.NextSteps.ToList();

        return nextSteps; 
    }

    public ReviewLevel GetRiskReviewLevel(int riskId)
    {
        using var dbContext = DalService.GetContext();

        var risk = dbContext.Risks.FirstOrDefault(r => r.Id == riskId);
        if(risk == null)
            throw new DataNotFoundException("local", "risks", new Exception($"Risk with id {riskId} not found"));
        
        var scoring = dbContext.RiskScorings.FirstOrDefault(rs => rs.Id == riskId);
        
        if (scoring == null)
            throw new DataNotFoundException("local", "risks", new Exception($"Risk scoring with id {riskId} not found"));

        var riskLevels = dbContext.RiskLevels.ToList();

        RiskLevel? foundRiskLevel = null;
        foreach (var riskLevel in riskLevels.OrderBy(rl => rl.Value))
        {
            if(scoring.CalculatedRisk  > Convert.ToSingle(riskLevel.Value )) foundRiskLevel = riskLevel;
            else break;
        }

        if (foundRiskLevel == null)
            throw new DataNotFoundException("local", "risks", new Exception($"Risk level with id {riskId} not found"));
        
        var reviewLevel = dbContext.ReviewLevels.FirstOrDefault(rl => rl.Name == foundRiskLevel.DisplayName);
        
        if(reviewLevel == null)
            throw new DataNotFoundException("local", "risks", new Exception($"Review level with id {riskId} not found"));

        return reviewLevel; 
    }

    public MgmtReview? GetRiskLastReview(int riskId)
    {
        using var dbContext = DalService.GetContext();
        
        RiskExists(riskId);

        var reviews = dbContext.MgmtReviews
            .Where(mr => mr.RiskId == riskId)
            .Include(mr => mr.ReviewNavigation)
            .Include(mr => mr.NextStepNavigation)
            .OrderBy(mr => mr.SubmissionDate).Reverse()
            .FirstOrDefault(); 

        return reviews;
    }

    public MgmtReview Create(MgmtReview review)
    {
        using var dbContext = DalService.GetContext();

        var dbReview = dbContext.MgmtReviews.Add(review);
        dbContext.SaveChanges();

        var dbObj = dbContext.MgmtReviews
            .Include(rev => rev.ReviewNavigation)
            .Include(rev => rev.NextStepNavigation)
            .FirstOrDefault(mr => mr.Id == dbReview.Entity.Id);
        
        return dbObj!;
    }
    
    public MgmtReview Update(MgmtReviewDto review)
    {
        using var dbContext = DalService.GetContext();

        var dbObj = dbContext.MgmtReviews.FirstOrDefault(mr => mr.Id == review.Id);
        
        if(dbObj == null)
            throw new DataNotFoundException("local", "mgmtReviews", new Exception($"MgmtReview with id {review.Id} not found"));

        
        dbObj = _mapper.Map<MgmtReview>(review);
        
        //var dbReview = dbContext.MgmtReviews.Update(dbObj);
        dbContext.SaveChanges();
        
        return dbObj;
    }

    public MgmtReview GetOne(int mgmtReviewId)
    {
        using var dbContext = DalService.GetContext();

        var dbObj = dbContext.MgmtReviews.FirstOrDefault(mr => mr.Id == mgmtReviewId);
        
        if(dbObj == null)
            throw new DataNotFoundException("local", "mgmtReviews", new Exception($"MgmtReview with id {mgmtReviewId} not found"));
        
        return dbObj;
    }
}