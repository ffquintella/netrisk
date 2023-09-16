using System;
using System.Data.Common;
using DAL.Entities;

namespace GUIClient.Models;

public class RiskReviewReportItem
{
    private readonly Risk _risk;

    public int Id => _risk.Id;
    public string Subject => _risk.Subject;
    public string Status => _risk.Status;
    public DateTime SubmissionDate => _risk.SubmissionDate;
    

    public RiskReviewReportItem(Risk risk)
    {
        _risk = risk;
    }

}