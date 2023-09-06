using AutoMapper;
using DAL;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class MgmtReviewsService: BaseService, IMgmtReviewsService
{
    public MgmtReviewsService(
        ILogger logger, 
        DALManager dalManager,
        IMapper mapper
    ): base(logger, dalManager, mapper)
    {
        
    }

    private void RiskExists(int riskId)
    {
        using var dbContext = DALManager.GetContext();
        // Check if risk exists 
        var risk = dbContext.Risks.FirstOrDefault(r => r.Id == riskId);
        if (risk == null)
            throw new DataNotFoundException("local", "risks", new Exception($"Risk with id {riskId} not found"));
    }

    public List<MgmtReview> GetRiskReviews(int riskId)
    {
        using var dbContext = DALManager.GetContext();
        
        RiskExists(riskId);

        var reviews = dbContext.MgmtReviews.Where(mr => mr.RiskId == riskId).ToList(); 

        return reviews;
    }

    public List<Review> GetReviewTypes()
    {
        using var dbContext = DALManager.GetContext();

        var reviews = dbContext.Reviews.ToList();

        return reviews; 
    }

    public ReviewLevel GetRiskReviewLevel(int riskId)
    {
        using var dbContext = DALManager.GetContext();

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
        using var dbContext = DALManager.GetContext();
        
        RiskExists(riskId);

        var reviews = dbContext.MgmtReviews
            .Where(mr => mr.RiskId == riskId)
            .Include(mr => mr.ReviewNavigation)
            .Include(mr => mr.NextStepNavigation)
            .OrderBy(mr => mr.SubmissionDate).Reverse()
            .FirstOrDefault(); 

        return reviews;
    }
}