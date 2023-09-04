﻿using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IMgmtReviewsService
{
    
    /// <summary>
    /// Gets a list of risk reviews 
    /// </summary>
    /// <param name="riskId"></param>
    /// <returns></returns>
    public List<MgmtReview> GetRiskReviews(int riskId);
    
    /// <summary>
    /// Gets the last review of a risk
    /// </summary>
    /// <param name="riskId"></param>
    /// <returns></returns>
    public MgmtReview? GetRiskLastReview(int riskId);
}