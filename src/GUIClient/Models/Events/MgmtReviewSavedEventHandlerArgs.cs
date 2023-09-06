using System;
using DAL.Entities;

namespace GUIClient.Models.Events;

public class MgmtReviewSavedEventHandlerArgs: EventArgs
{
    public MgmtReview MgmtReview { get; set; }
    
    public MgmtReviewSavedEventHandlerArgs(MgmtReview mgmtReview)
    {
        MgmtReview = mgmtReview;
    }
}