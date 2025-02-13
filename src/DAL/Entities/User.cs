﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace DAL.Entities;

public partial class User
{
    public int Value { get; set; }

    public bool? Enabled { get; set; }

    public sbyte Lockout { get; set; }

    public string Type { get; set; } = null!;

    //public byte[] Username { get; set; } = null!;
    
    [Column(TypeName = "VARCHAR")]
    [StringLength(250)]
    public string Login { get; set; } = Guid.NewGuid().ToString();

    public string Name { get; set; } = null!;

    public byte[] Email { get; set; } = null!;

    public string? Salt { get; set; }

    [JsonIgnore]
    public byte[] Password { get; set; } = null!;

    public DateTime? LastLogin { get; set; }

    public DateTime LastPasswordChangeDate { get; set; }

    public int RoleId { get; set; }

    public string? Lang { get; set; }

    public bool Admin { get; set; }

    public int MultiFactor { get; set; }

    public sbyte ChangePassword { get; set; }

    public int? Manager { get; set; }

    public virtual ICollection<IncidentResponsePlan> IncidentResponsePlans { get; set; } = new List<IncidentResponsePlan>();
    public virtual ICollection<IncidentResponsePlan> IncidentResponsePlansUpdated { get; set; } = new List<IncidentResponsePlan>();
    
    public virtual ICollection<IncidentResponsePlanTask> IncidentResponseTasksCreated { get; set; } = new List<IncidentResponsePlanTask>();
    
    public virtual ICollection<IncidentResponsePlanTask> IncidentResponseTasksUpdated { get; set; } = new List<IncidentResponsePlanTask>();
    
    public virtual ICollection<AssessmentRun> AssessmentRuns { get; set; } = new List<AssessmentRun>();

    public virtual ICollection<Comment> Comments { get; set; } = new List<Comment>();

    public virtual ICollection<FixRequest> FixRequestLastReportingUsers { get; set; } = new List<FixRequest>();

    public virtual ICollection<FixRequest> FixRequestRequestingUsers { get; set; } = new List<FixRequest>();

    public virtual ICollection<Job> Jobs { get; set; } = new List<Job>();

    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();

    public virtual ICollection<MgmtReview> MgmtReviews { get; set; } = new List<MgmtReview>();

    public virtual ICollection<Mitigation> MitigationMitigationOwnerNavigations { get; set; } = new List<Mitigation>();

    public virtual ICollection<Mitigation> MitigationSubmittedByNavigations { get; set; } = new List<Mitigation>();

    public virtual ICollection<NrAction> NrActions { get; set; } = new List<NrAction>();

    public virtual ICollection<Report> Reports { get; set; } = new List<Report>();

    public virtual Role Role { get; set; } = null!;

    public virtual ICollection<Vulnerability> Vulnerabilities { get; set; } = new List<Vulnerability>();

    public virtual ICollection<Permission> Permissions { get; set; } = new List<Permission>();

    public virtual ICollection<Team> Teams { get; set; } = new List<Team>();
    
    public virtual ICollection<Incident> IncidentsCreated { get; set; } = new List<Incident>();
    public virtual ICollection<Incident> IncidentsUpdated { get; set; } = new List<Incident>();
    
    public virtual ICollection<IncidentResponsePlanTaskExecution> IncidentResponsePlanTaskExecutions { get; set; } = new List<IncidentResponsePlanTaskExecution>();
    public virtual ICollection<IncidentResponsePlanTaskExecution> IncidentResponsePlanTaskExecutionsLastUpdated { get; set; } = new List<IncidentResponsePlanTaskExecution>();
    public virtual ICollection<IncidentResponsePlanExecution> IncidentResponsePlanExecutions { get; set; } = new List<IncidentResponsePlanExecution>();
    public virtual ICollection<IncidentResponsePlanExecution> IncidentResponsePlanExecutionsLastUpdated { get; set; } = new List<IncidentResponsePlanExecution>();
    
    public virtual ICollection<Incident> IncidentsAssignedTo { get; set; } = new List<Incident>();
}
