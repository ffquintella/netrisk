﻿using AutoMapper;
using DAL;
using DAL.Entities;
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

    public MgmtReview? GetRiskLastReview(int riskId)
    {
        using var dbContext = DALManager.GetContext();
        
        RiskExists(riskId);

        var reviews = dbContext.MgmtReviews
            .Where(mr => mr.RiskId == riskId)
            .OrderBy(mr => mr.SubmissionDate).Reverse()
            .FirstOrDefault(); 

        return reviews;
    }
}