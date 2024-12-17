using System;
using DAL.Entities;

namespace GUIClient.Events;

public class IncidentResponsePlanTaskEventArgs: EventArgs
{
    
    public int PlanId { get; set; }
    public IncidentResponsePlanTask Task { get; set; }
}