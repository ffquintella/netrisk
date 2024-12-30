using System;
using System.Reflection.Emit;
using DAL.Entities;
using GUIClient.Models;

namespace GUIClient.Events;

public class IncidentEventArgs: EventArgs
{
    public OperationType OperationType { get; set; }
    public Incident Incident { get; set; }
}