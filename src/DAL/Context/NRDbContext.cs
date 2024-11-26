using System;
using System.Collections.Generic;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DAL.Context;

public partial class NRDbContext : DbContext
{
    public NRDbContext(DbContextOptions<NRDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<ApiKey> ApiKeys { get; set; }

    public virtual DbSet<Assessment> Assessments { get; set; }

    public virtual DbSet<AssessmentAnswer> AssessmentAnswers { get; set; }

    public virtual DbSet<AssessmentQuestion> AssessmentQuestions { get; set; }

    public virtual DbSet<AssessmentRun> AssessmentRuns { get; set; }

    public virtual DbSet<AssessmentRunsAnswer> AssessmentRunsAnswers { get; set; }

    public virtual DbSet<Audit> Audits { get; set; }

    public virtual DbSet<Category> Categories { get; set; }

    public virtual DbSet<ClientRegistration> ClientRegistrations { get; set; }

    public virtual DbSet<CloseReason> CloseReasons { get; set; }

    public virtual DbSet<Closure> Closures { get; set; }

    public virtual DbSet<Comment> Comments { get; set; }

    public virtual DbSet<ContributingRisk> ContributingRisks { get; set; }

    public virtual DbSet<ContributingRisksImpact> ContributingRisksImpacts { get; set; }

    public virtual DbSet<ContributingRisksLikelihood> ContributingRisksLikelihoods { get; set; }

    public virtual DbSet<ControlClass> ControlClasses { get; set; }

    public virtual DbSet<ControlMaturity> ControlMaturities { get; set; }

    public virtual DbSet<ControlPhase> ControlPhases { get; set; }

    public virtual DbSet<ControlPriority> ControlPriorities { get; set; }

    public virtual DbSet<ControlType> ControlTypes { get; set; }

    public virtual DbSet<CustomRiskModelValue> CustomRiskModelValues { get; set; }

    public virtual DbSet<EntitiesProperty> EntitiesProperties { get; set; }

    public virtual DbSet<Entity> Entities { get; set; }

    public virtual DbSet<FailedLoginAttempt> FailedLoginAttempts { get; set; }

    public virtual DbSet<Family> Families { get; set; }

    public virtual DbSet<FileType> FileTypes { get; set; }

    public virtual DbSet<FileTypeExtension> FileTypeExtensions { get; set; }

    public virtual DbSet<FixRequest> FixRequests { get; set; }

    public virtual DbSet<Framework> Frameworks { get; set; }

    public virtual DbSet<FrameworkControl> FrameworkControls { get; set; }

    public virtual DbSet<FrameworkControlMapping> FrameworkControlMappings { get; set; }

    public virtual DbSet<FrameworkControlTest> FrameworkControlTests { get; set; }

    public virtual DbSet<FrameworkControlTestAudit> FrameworkControlTestAudits { get; set; }

    public virtual DbSet<FrameworkControlTestComment> FrameworkControlTestComments { get; set; }

    public virtual DbSet<FrameworkControlTestResult> FrameworkControlTestResults { get; set; }

    public virtual DbSet<FrameworkControlTestResultsToRisk> FrameworkControlTestResultsToRisks { get; set; }

    public virtual DbSet<FrameworkControlToFramework> FrameworkControlToFrameworks { get; set; }

    public virtual DbSet<FrameworkControlTypeMapping> FrameworkControlTypeMappings { get; set; }

    public virtual DbSet<Host> Hosts { get; set; }

    public virtual DbSet<HostsService> HostsServices { get; set; }

    public virtual DbSet<Impact> Impacts { get; set; }

    public virtual DbSet<Job> Jobs { get; set; }

    public virtual DbSet<Likelihood> Likelihoods { get; set; }

    public virtual DbSet<Link> Links { get; set; }

    public virtual DbSet<Location> Locations { get; set; }

    public virtual DbSet<Message> Messages { get; set; }

    public virtual DbSet<MgmtReview> MgmtReviews { get; set; }

    public virtual DbSet<Mitigation> Mitigations { get; set; }

    public virtual DbSet<MitigationAcceptUser> MitigationAcceptUsers { get; set; }

    public virtual DbSet<MitigationCost> MitigationCosts { get; set; }

    public virtual DbSet<MitigationEffort> MitigationEfforts { get; set; }

    public virtual DbSet<MitigationToControl> MitigationToControls { get; set; }

    public virtual DbSet<MitigationToTeam> MitigationToTeams { get; set; }

    public virtual DbSet<NextStep> NextSteps { get; set; }

    public virtual DbSet<NrAction> NrActions { get; set; }

    public virtual DbSet<NrFile> NrFiles { get; set; }

    public virtual DbSet<PendingRisk> PendingRisks { get; set; }

    public virtual DbSet<Permission> Permissions { get; set; }

    public virtual DbSet<PermissionGroup> PermissionGroups { get; set; }

    public virtual DbSet<PermissionToPermissionGroup> PermissionToPermissionGroups { get; set; }

    public virtual DbSet<PlanningStrategy> PlanningStrategies { get; set; }

    public virtual DbSet<QuestionnairePendingRisk> QuestionnairePendingRisks { get; set; }

    public virtual DbSet<Regulation> Regulations { get; set; }

    public virtual DbSet<Report> Reports { get; set; }

    public virtual DbSet<ResidualRiskScoringHistory> ResidualRiskScoringHistories { get; set; }

    public virtual DbSet<Review> Reviews { get; set; }

    public virtual DbSet<ReviewLevel> ReviewLevels { get; set; }

    public virtual DbSet<Risk> Risks { get; set; }

    public virtual DbSet<RiskCatalog> RiskCatalogs { get; set; }

    public virtual DbSet<RiskFunction> RiskFunctions { get; set; }

    public virtual DbSet<RiskGrouping> RiskGroupings { get; set; }

    public virtual DbSet<RiskLevel> RiskLevels { get; set; }

    public virtual DbSet<RiskScoring> RiskScorings { get; set; }

    public virtual DbSet<RiskScoringContributingImpact> RiskScoringContributingImpacts { get; set; }

    public virtual DbSet<RiskScoringHistory> RiskScoringHistories { get; set; }

    public virtual DbSet<RiskToAdditionalStakeholder> RiskToAdditionalStakeholders { get; set; }

    public virtual DbSet<RiskToLocation> RiskToLocations { get; set; }

    public virtual DbSet<RiskToTeam> RiskToTeams { get; set; }

    public virtual DbSet<RiskToTechnology> RiskToTechnologies { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<ScoringMethod> ScoringMethods { get; set; }

    public virtual DbSet<Setting> Settings { get; set; }

    public virtual DbSet<Source> Sources { get; set; }

    public virtual DbSet<Status> Statuses { get; set; }

    public virtual DbSet<Tag> Tags { get; set; }

    public virtual DbSet<TagsTaggee> TagsTaggees { get; set; }

    public virtual DbSet<Team> Teams { get; set; }

    public virtual DbSet<Technology> Technologies { get; set; }

    public virtual DbSet<TestResult> TestResults { get; set; }

    public virtual DbSet<TestStatus> TestStatuses { get; set; }

    public virtual DbSet<ThreatCatalog> ThreatCatalogs { get; set; }

    public virtual DbSet<ThreatGrouping> ThreatGroupings { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<UserPassHistory> UserPassHistories { get; set; }

    public virtual DbSet<UserPassReuseHistory> UserPassReuseHistories { get; set; }

    public virtual DbSet<Vulnerability> Vulnerabilities { get; set; }
    
    public virtual DbSet<IncidentResponsePlanExecution> IncidentResponsePlanExecutions { get; set; }
    public virtual DbSet<IncidentResponsePlan> IncidentResponsePlans { get; set; }
    public virtual DbSet<IncidentResponsePlanTask> IncidentResponsePlanTasks { get; set; }
    
    public virtual DbSet<IncidentResponsePlanTaskExecution> IncidentResponsePlanTaskExecutions { get; set; }
    
    public virtual DbSet<Incident> Incidents { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .UseCollation("utf8mb4_general_ci")
            .HasCharSet("utf8mb4");

        modelBuilder.Entity<ApiKey>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("api_keys")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.ClientIp, "idx_api_keys_ip");

            entity.HasIndex(e => e.Value, "idx_api_keys_value").IsUnique();

            entity.Property(e => e.Id)
                .HasColumnType("int(6) unsigned")
                .HasColumnName("id");
            entity.Property(e => e.ClientIp).HasColumnName("client_ip");
            entity.Property(e => e.CreationDate)
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("timestamp")
                .HasColumnName("creation_date");
            entity.Property(e => e.Name)
                .HasMaxLength(30)
                .HasColumnName("name");
            entity.Property(e => e.Status)
                .HasMaxLength(10)
                .HasColumnName("status");
            entity.Property(e => e.Value)
                .HasMaxLength(50)
                .HasColumnName("value");
        });

        modelBuilder.Entity<Assessment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("assessments")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.Created)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("timestamp")
                .HasColumnName("created");
            entity.Property(e => e.Name)
                .HasMaxLength(200)
                .HasColumnName("name");
        });

        modelBuilder.Entity<AssessmentAnswer>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("assessment_answers")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.AssessmentId, "fk_assessment_answer");

            entity.HasIndex(e => e.QuestionId, "fk_question_answer");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.Answer)
                .HasMaxLength(200)
                .HasColumnName("answer");
            entity.Property(e => e.AssessmentId)
                .HasColumnType("int(11)")
                .HasColumnName("assessment_id");
            entity.Property(e => e.AssessmentScoringId)
                .HasColumnType("int(11)")
                .HasColumnName("assessment_scoring_id");
            entity.Property(e => e.Order)
                .HasDefaultValueSql("'999999'")
                .HasColumnType("int(11)")
                .HasColumnName("order");
            entity.Property(e => e.QuestionId)
                .HasColumnType("int(11)")
                .HasColumnName("question_id");
            entity.Property(e => e.RiskOwner)
                .HasColumnType("int(11)")
                .HasColumnName("risk_owner");
            entity.Property(e => e.RiskScore).HasColumnName("risk_score");
            entity.Property(e => e.RiskSubject)
                .HasColumnType("blob")
                .HasColumnName("risk_subject");
            entity.Property(e => e.SubmitRisk).HasColumnName("submit_risk");

            entity.HasOne(d => d.Assessment).WithMany(p => p.AssessmentAnswers)
                .HasForeignKey(d => d.AssessmentId)
                .HasConstraintName("fk_assessment_answer");

            entity.HasOne(d => d.Question).WithMany(p => p.AssessmentAnswers)
                .HasForeignKey(d => d.QuestionId)
                .HasConstraintName("fk_question_answer");
        });

        modelBuilder.Entity<AssessmentQuestion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("assessment_questions")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.AssessmentId, "fk_assessment_question");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.AssessmentId)
                .HasColumnType("int(11)")
                .HasColumnName("assessment_id");
            entity.Property(e => e.Order)
                .HasDefaultValueSql("'999999'")
                .HasColumnType("int(11)")
                .HasColumnName("order");
            entity.Property(e => e.Question)
                .HasMaxLength(1000)
                .HasColumnName("question");

            entity.HasOne(d => d.Assessment).WithMany(p => p.AssessmentQuestions)
                .HasForeignKey(d => d.AssessmentId)
                .HasConstraintName("fk_assessment_question");
        });

        modelBuilder.Entity<AssessmentRun>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("assessment_runs");

            entity.HasIndex(e => e.AnalystId, "fkAnalystId");

            entity.HasIndex(e => e.AssessmentId, "fkAssessment");

            entity.HasIndex(e => e.EntityId, "fkEntity");

            entity.HasIndex(e => e.HostId, "fkHost");

            entity.HasIndex(e => e.Status, "idxStatus");

            entity.Property(e => e.Id).HasColumnType("int(11)");
            entity.Property(e => e.AnalystId).HasColumnType("int(11)");
            entity.Property(e => e.AssessmentId).HasColumnType("int(11)");
            entity.Property(e => e.Comments).HasColumnType("text");
            entity.Property(e => e.EntityId).HasColumnType("int(11)");
            entity.Property(e => e.HostId).HasColumnType("int(11)");
            entity.Property(e => e.RunDate).HasColumnType("datetime");
            entity.Property(e => e.Status).HasColumnType("int(11)");

            entity.HasOne(d => d.Analyst).WithMany(p => p.AssessmentRuns)
                .HasForeignKey(d => d.AnalystId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fkAnalystId");

            entity.HasOne(d => d.Assessment).WithMany(p => p.AssessmentRuns)
                .HasForeignKey(d => d.AssessmentId)
                .HasConstraintName("fkAssessment");

            entity.HasOne(d => d.Entity).WithMany(p => p.AssessmentRuns)
                .HasForeignKey(d => d.EntityId)
                .HasConstraintName("fkEntity");

            entity.HasOne(d => d.Host).WithMany(p => p.AssessmentRuns)
                .HasForeignKey(d => d.HostId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fkHost");
        });

        modelBuilder.Entity<AssessmentRunsAnswer>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("assessment_runs_answers");

            entity.HasIndex(e => e.AnswerId, "fkAnswerId");

            entity.HasIndex(e => e.QuestionId, "fkQuestionId");

            entity.HasIndex(e => e.RunId, "fkRunId");

            entity.Property(e => e.Id).HasColumnType("int(11)");
            entity.Property(e => e.AnswerId).HasColumnType("int(11)");
            entity.Property(e => e.QuestionId).HasColumnType("int(11)");
            entity.Property(e => e.RunId).HasColumnType("int(11)");

            entity.HasOne(d => d.Answer).WithMany(p => p.AssessmentRunsAnswers)
                .HasForeignKey(d => d.AnswerId)
                .HasConstraintName("fkAnswerId");

            entity.HasOne(d => d.Question).WithMany(p => p.AssessmentRunsAnswers)
                .HasForeignKey(d => d.QuestionId)
                .HasConstraintName("fkQuestionId");

            entity.HasOne(d => d.Run).WithMany(p => p.AssessmentRunsAnswers)
                .HasForeignKey(d => d.RunId)
                .HasConstraintName("fkRunId");
        });

        modelBuilder.Entity<Audit>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("audit");

            entity.HasIndex(e => e.AffectedColumns, "idx_audit_cols").HasAnnotation("MySql:IndexPrefixLength", new[] { 768 });

            entity.HasIndex(e => e.DateTime, "idx_audit_date");

            entity.HasIndex(e => e.NewValues, "idx_audit_newVal").HasAnnotation("MySql:FullTextIndex", true);

            entity.HasIndex(e => e.OldValues, "idx_audit_oldValues").HasAnnotation("MySql:FullTextIndex", true);

            entity.HasIndex(e => e.PrimaryKey, "idx_audit_pk");

            entity.HasIndex(e => e.TableName, "idx_audit_table");

            entity.HasIndex(e => e.Type, "idx_audit_type");

            entity.HasIndex(e => e.UserId, "idx_audit_userid");

            entity.Property(e => e.Id).HasColumnType("int(11)");
            entity.Property(e => e.AffectedColumns).HasColumnType("text");
            entity.Property(e => e.DateTime).HasColumnType("datetime");
            entity.Property(e => e.NewValues)
                .HasColumnType("longtext")
                .UseCollation("utf8mb4_unicode_ci");
            entity.Property(e => e.OldValues)
                .HasColumnType("longtext")
                .UseCollation("utf8mb4_unicode_ci");
            entity.Property(e => e.UserId).HasColumnType("int(11)");
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("category")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .HasColumnName("name");
        });

        modelBuilder.Entity<ClientRegistration>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("client_registration")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.ExternalId, "ExternalId");

            entity.Property(e => e.Id).HasColumnType("int(11)");
            entity.Property(e => e.Hostname).HasMaxLength(255);
            entity.Property(e => e.LastVerificationDate)
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("'0000-00-00 00:00:00'")
                .HasColumnType("datetime");
            entity.Property(e => e.LoggedAccount).HasMaxLength(255);
            entity.Property(e => e.Name).HasMaxLength(255);
            entity.Property(e => e.RegistrationDate)
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("'0000-00-00 00:00:00'")
                .HasColumnType("datetime");
            entity.Property(e => e.Status)
                .HasMaxLength(255)
                .HasDefaultValueSql("'requested'");
        });

        modelBuilder.Entity<CloseReason>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("close_reason")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Closure>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("closures")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.CloseReason, "closures_close_reason_idx");

            entity.HasIndex(e => e.UserId, "closures_user_id_idx");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.CloseReason)
                .HasColumnType("int(11)")
                .HasColumnName("close_reason");
            entity.Property(e => e.ClosureDate)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("timestamp")
                .HasColumnName("closure_date");
            entity.Property(e => e.Note)
                .HasColumnType("text")
                .HasColumnName("note");
            entity.Property(e => e.RiskId)
                .HasColumnType("int(11)")
                .HasColumnName("risk_id");
            entity.Property(e => e.UserId)
                .HasColumnType("int(11)")
                .HasColumnName("user_id");
        });

        modelBuilder.Entity<Comment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("comments");

            entity.HasIndex(e => e.FixRequestId, "fk_fix_request_comments");

            entity.HasIndex(e => e.HostId, "fk_host_id_comments");

            entity.HasIndex(e => e.RiskId, "fk_risk_id_comments");

            entity.HasIndex(e => e.UserId, "fk_user_id_comments");

            entity.HasIndex(e => e.VulnerabilityId, "fk_vulnerability_id_comments");

            entity.HasIndex(e => e.CommenterName, "idx-commenterName");

            entity.HasIndex(e => e.Text, "idx-full-text").HasAnnotation("MySql:FullTextIndex", true);

            entity.HasIndex(e => e.Type, "idx-type-comments");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.Date)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime");
            entity.Property(e => e.FixRequestId).HasColumnType("int(11)");
            entity.Property(e => e.HostId).HasColumnType("int(11)");
            entity.Property(e => e.IsAnonymous).HasColumnType("tinyint(4)");
            entity.Property(e => e.ReplyTo).HasColumnType("int(11)");
            entity.Property(e => e.RiskId).HasColumnType("int(11)");
            entity.Property(e => e.Text).HasColumnType("text");
            entity.Property(e => e.UserId).HasColumnType("int(11)");
            entity.Property(e => e.VulnerabilityId).HasColumnType("int(11)");

            entity.HasOne(d => d.FixRequest).WithMany(p => p.Comments)
                .HasForeignKey(d => d.FixRequestId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_fix_request_comments");

            entity.HasOne(d => d.Host).WithMany(p => p.Comments)
                .HasForeignKey(d => d.HostId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_host_id_comments");

            entity.HasOne(d => d.Risk).WithMany(p => p.Comments)
                .HasForeignKey(d => d.RiskId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_risk_id_comments");

            entity.HasOne(d => d.User).WithMany(p => p.Comments)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_user_id_comments");

            entity.HasOne(d => d.Vulnerability).WithMany(p => p.CommentsNavigation)
                .HasForeignKey(d => d.VulnerabilityId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_vulnerability_id_comments");
        });

        modelBuilder.Entity<ContributingRisk>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("contributing_risks")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.Subject)
                .HasMaxLength(1000)
                .HasColumnName("subject");
            entity.Property(e => e.Weight).HasColumnName("weight");
        });

        modelBuilder.Entity<ContributingRisksImpact>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("contributing_risks_impact")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.ContributingRisksId, "contributing_risks_id");

            entity.HasIndex(e => new { e.ContributingRisksId, e.Value }, "cri_index");

            entity.HasIndex(e => e.Value, "cri_value_idx");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.ContributingRisksId)
                .HasColumnType("int(11)")
                .HasColumnName("contributing_risks_id");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
        });

        modelBuilder.Entity<ContributingRisksLikelihood>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("contributing_risks_likelihood")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.Value, "crl_index");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
        });

        modelBuilder.Entity<ControlClass>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("control_class")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Name)
                .HasColumnType("mediumtext")
                .HasColumnName("name");
        });

        modelBuilder.Entity<ControlMaturity>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("control_maturity")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Value)
                .ValueGeneratedNever()
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Name)
                .HasColumnType("mediumtext")
                .HasColumnName("name");
        });

        modelBuilder.Entity<ControlPhase>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("control_phase")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Name)
                .HasColumnType("mediumtext")
                .HasColumnName("name");
        });

        modelBuilder.Entity<ControlPriority>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("control_priority")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Name)
                .HasColumnType("mediumtext")
                .HasColumnName("name");
        });

        modelBuilder.Entity<ControlType>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("control_type")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Name)
                .HasColumnType("mediumtext")
                .HasColumnName("name");
        });

        modelBuilder.Entity<CustomRiskModelValue>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("custom_risk_model_values")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => new { e.Impact, e.Likelihood }, "impact_likelihood_unique").IsUnique();

            entity.Property(e => e.Impact)
                .HasColumnType("int(11)")
                .HasColumnName("impact");
            entity.Property(e => e.Likelihood)
                .HasColumnType("int(11)")
                .HasColumnName("likelihood");
            entity.Property(e => e.Value)
                .HasColumnType("double(3,1)")
                .HasColumnName("value");
        });

        modelBuilder.Entity<EntitiesProperty>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("entities_properties");

            entity.HasIndex(e => e.Entity, "fk_entity");

            entity.HasIndex(e => e.Name, "idx_name");

            entity.HasIndex(e => e.Value, "idx_value").HasAnnotation("MySql:FullTextIndex", true);

            entity.Property(e => e.Id).HasColumnType("int(11)");
            entity.Property(e => e.Entity).HasColumnType("int(11)");
            entity.Property(e => e.OldValue).HasColumnType("text");
            entity.Property(e => e.Type).HasMaxLength(255);
            entity.Property(e => e.Value).HasColumnType("text");

            entity.HasOne(d => d.EntityNavigation).WithMany(p => p.EntitiesProperties)
                .HasForeignKey(d => d.Entity)
                .HasConstraintName("fk_entity");
        });

        modelBuilder.Entity<Entity>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("entities");

            entity.HasIndex(e => e.Parent, "fk_parent");

            entity.HasIndex(e => e.DefinitionName, "idx_definition_name");

            entity.Property(e => e.Id).HasColumnType("int(11)");
            entity.Property(e => e.Created)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("timestamp");
            entity.Property(e => e.CreatedBy).HasColumnType("int(11)");
            entity.Property(e => e.DefinitionVersion).HasMaxLength(15);
            entity.Property(e => e.Parent).HasColumnType("int(11)");
            entity.Property(e => e.Status).HasMaxLength(15);
            entity.Property(e => e.Updated)
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("timestamp");
            entity.Property(e => e.UpdatedBy).HasColumnType("int(11)");

            entity.HasOne(d => d.ParentNavigation).WithMany(p => p.InverseParentNavigation)
                .HasForeignKey(d => d.Parent)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_parent");
            

        });

        modelBuilder.Entity<FailedLoginAttempt>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("failed_login_attempts")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.Date)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("timestamp")
                .HasColumnName("date");
            entity.Property(e => e.Expired)
                .HasDefaultValueSql("'0'")
                .HasColumnType("tinyint(4)")
                .HasColumnName("expired");
            entity.Property(e => e.Ip)
                .HasMaxLength(15)
                .HasDefaultValueSql("'0.0.0.0'")
                .HasColumnName("ip");
            entity.Property(e => e.UserId)
                .HasColumnType("int(11)")
                .HasColumnName("user_id");
        });

        modelBuilder.Entity<Family>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("family")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
        });

        modelBuilder.Entity<FileType>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("file_types")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.Name, "name").IsUnique();

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Name)
                .HasMaxLength(250)
                .HasColumnName("name");
        });

        modelBuilder.Entity<FileTypeExtension>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("file_type_extensions")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.Name, "name").IsUnique();

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Name)
                .HasMaxLength(10)
                .HasColumnName("name");
        });

        modelBuilder.Entity<FixRequest>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("FixRequest");

            entity.HasIndex(e => e.FixTeamId, "fk_fixteam");

            entity.HasIndex(e => e.LastReportingUserId, "fk_lastReportingUser");

            entity.HasIndex(e => e.RequestingUserId, "fk_requesting_user_id");

            entity.HasIndex(e => e.VulnerabilityId, "fk_vulnerability");

            entity.HasIndex(e => e.Identifier, "idx_identifier").IsUnique();

            entity.Property(e => e.Id).HasColumnType("int(11)");
            entity.Property(e => e.CreationDate)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime");
            entity.Property(e => e.FixDate).HasColumnType("datetime");
            entity.Property(e => e.FixTeamId).HasColumnType("int(11)");
            entity.Property(e => e.LastInteraction)
                .ValueGeneratedOnAddOrUpdate()
                .HasColumnType("datetime");
            entity.Property(e => e.LastReportingUserId).HasColumnType("int(11)");
            entity.Property(e => e.RequestingUserId).HasColumnType("int(11)");
            entity.Property(e => e.SingleFixDestination).HasMaxLength(255);
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'1'")
                .HasColumnType("int(11)");
            entity.Property(e => e.TargetDate).HasColumnType("datetime");
            entity.Property(e => e.VulnerabilityId).HasColumnType("int(11)");

            entity.HasOne(d => d.FixTeam).WithMany(p => p.FixRequests)
                .HasForeignKey(d => d.FixTeamId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_fixteam");

            entity.HasOne(d => d.LastReportingUser).WithMany(p => p.FixRequestLastReportingUsers)
                .HasForeignKey(d => d.LastReportingUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_lastReportingUser");

            entity.HasOne(d => d.RequestingUser).WithMany(p => p.FixRequestRequestingUsers)
                .HasForeignKey(d => d.RequestingUserId)
                .HasConstraintName("fk_requesting_user_id");

            entity.HasOne(d => d.Vulnerability).WithMany(p => p.FixRequests)
                .HasForeignKey(d => d.VulnerabilityId)
                .HasConstraintName("fk_vulnerability");
        });

        modelBuilder.Entity<Framework>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("frameworks")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Description)
                .HasColumnType("blob")
                .HasColumnName("description");
            entity.Property(e => e.DesiredFrequency)
                .HasColumnType("int(11)")
                .HasColumnName("desired_frequency");
            entity.Property(e => e.LastAuditDate).HasColumnName("last_audit_date");
            entity.Property(e => e.Name)
                .HasColumnType("blob")
                .HasColumnName("name");
            entity.Property(e => e.NextAuditDate).HasColumnName("next_audit_date");
            entity.Property(e => e.Order)
                .HasColumnType("int(11)")
                .HasColumnName("order");
            entity.Property(e => e.Parent)
                .HasColumnType("int(11)")
                .HasColumnName("parent");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'1'")
                .HasColumnType("int(11)")
                .HasColumnName("status");
        });

        modelBuilder.Entity<FrameworkControl>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("framework_controls")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.Deleted, "framework_controls_deleted_idx");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.ControlClass)
                .HasColumnType("int(11)")
                .HasColumnName("control_class");
            entity.Property(e => e.ControlMaturity)
                .HasColumnType("int(11)")
                .HasColumnName("control_maturity");
            entity.Property(e => e.ControlNumber)
                .HasMaxLength(100)
                .HasColumnName("control_number");
            entity.Property(e => e.ControlOwner)
                .HasColumnType("int(11)")
                .HasColumnName("control_owner");
            entity.Property(e => e.ControlPhase)
                .HasColumnType("int(11)")
                .HasColumnName("control_phase");
            entity.Property(e => e.ControlPriority)
                .HasColumnType("int(11)")
                .HasColumnName("control_priority");
            entity.Property(e => e.ControlStatus)
                .HasDefaultValueSql("'1'")
                .HasColumnName("control_status");
            entity.Property(e => e.Deleted)
                .HasColumnType("tinyint(4)")
                .HasColumnName("deleted");
            entity.Property(e => e.Description)
                .HasColumnType("blob")
                .HasColumnName("description");
            entity.Property(e => e.DesiredFrequency)
                .HasColumnType("int(11)")
                .HasColumnName("desired_frequency");
            entity.Property(e => e.DesiredMaturity)
                .HasColumnType("int(11)")
                .HasColumnName("desired_maturity");
            entity.Property(e => e.Family)
                .HasColumnType("int(11)")
                .HasColumnName("family");
            entity.Property(e => e.LastAuditDate).HasColumnName("last_audit_date");
            entity.Property(e => e.LongName)
                .HasColumnType("blob")
                .HasColumnName("long_name");
            entity.Property(e => e.MitigationPercent)
                .HasColumnType("int(11)")
                .HasColumnName("mitigation_percent");
            entity.Property(e => e.NextAuditDate).HasColumnName("next_audit_date");
            entity.Property(e => e.ShortName)
                .HasMaxLength(1000)
                .HasColumnName("short_name");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'1'")
                .HasColumnType("int(11)")
                .HasColumnName("status");
            entity.Property(e => e.SubmissionDate)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("timestamp")
                .HasColumnName("submission_date");
            entity.Property(e => e.SupplementalGuidance)
                .HasColumnType("blob")
                .HasColumnName("supplemental_guidance");
        });

        modelBuilder.Entity<FrameworkControlMapping>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("framework_control_mappings")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.ControlId, "control_id");

            entity.HasIndex(e => e.Framework, "framework");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.ControlId)
                .HasColumnType("int(11)")
                .HasColumnName("control_id");
            entity.Property(e => e.Framework)
                .HasColumnType("int(11)")
                .HasColumnName("framework");
            entity.Property(e => e.ReferenceName)
                .HasMaxLength(200)
                .HasColumnName("reference_name");
        });

        modelBuilder.Entity<FrameworkControlTest>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("framework_control_tests")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.Id, "id").IsUnique();

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.AdditionalStakeholders)
                .HasMaxLength(500)
                .HasColumnName("additional_stakeholders");
            entity.Property(e => e.ApproximateTime)
                .HasColumnType("int(11)")
                .HasColumnName("approximate_time");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.DesiredFrequency)
                .HasColumnType("int(11)")
                .HasColumnName("desired_frequency");
            entity.Property(e => e.ExpectedResults)
                .HasColumnType("mediumtext")
                .HasColumnName("expected_results");
            entity.Property(e => e.FrameworkControlId)
                .HasColumnType("int(11)")
                .HasColumnName("framework_control_id");
            entity.Property(e => e.LastDate).HasColumnName("last_date");
            entity.Property(e => e.Name)
                .HasColumnType("mediumtext")
                .HasColumnName("name");
            entity.Property(e => e.NextDate).HasColumnName("next_date");
            entity.Property(e => e.Objective)
                .HasColumnType("mediumtext")
                .HasColumnName("objective");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'1'")
                .HasColumnType("int(11)")
                .HasColumnName("status");
            entity.Property(e => e.TestFrequency)
                .HasColumnType("int(11)")
                .HasColumnName("test_frequency");
            entity.Property(e => e.TestSteps)
                .HasColumnType("mediumtext")
                .HasColumnName("test_steps");
            entity.Property(e => e.Tester)
                .HasColumnType("int(11)")
                .HasColumnName("tester");
        });

        modelBuilder.Entity<FrameworkControlTestAudit>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("framework_control_test_audits")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.ApproximateTime)
                .HasColumnType("int(11)")
                .HasColumnName("approximate_time");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("datetime")
                .HasColumnName("created_at");
            entity.Property(e => e.DesiredFrequency)
                .HasColumnType("int(11)")
                .HasColumnName("desired_frequency");
            entity.Property(e => e.ExpectedResults)
                .HasColumnType("mediumtext")
                .HasColumnName("expected_results");
            entity.Property(e => e.FrameworkControlId)
                .HasColumnType("int(11)")
                .HasColumnName("framework_control_id");
            entity.Property(e => e.LastDate).HasColumnName("last_date");
            entity.Property(e => e.Name)
                .HasColumnType("mediumtext")
                .HasColumnName("name");
            entity.Property(e => e.NextDate).HasColumnName("next_date");
            entity.Property(e => e.Objective)
                .HasColumnType("mediumtext")
                .HasColumnName("objective");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'1'")
                .HasColumnType("int(11)")
                .HasColumnName("status");
            entity.Property(e => e.TestFrequency)
                .HasColumnType("int(11)")
                .HasColumnName("test_frequency");
            entity.Property(e => e.TestId)
                .HasColumnType("int(11)")
                .HasColumnName("test_id");
            entity.Property(e => e.TestSteps)
                .HasColumnType("mediumtext")
                .HasColumnName("test_steps");
            entity.Property(e => e.Tester)
                .HasColumnType("int(11)")
                .HasColumnName("tester");
        });

        modelBuilder.Entity<FrameworkControlTestComment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("framework_control_test_comments")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.Comment)
                .HasColumnType("mediumtext")
                .HasColumnName("comment");
            entity.Property(e => e.Date)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("timestamp")
                .HasColumnName("date");
            entity.Property(e => e.TestAuditId)
                .HasColumnType("int(11)")
                .HasColumnName("test_audit_id");
            entity.Property(e => e.User)
                .HasColumnType("int(11)")
                .HasColumnName("user");
        });

        modelBuilder.Entity<FrameworkControlTestResult>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("framework_control_test_results")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.LastUpdated)
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("timestamp")
                .HasColumnName("last_updated");
            entity.Property(e => e.SubmissionDate)
                .HasColumnType("datetime")
                .HasColumnName("submission_date");
            entity.Property(e => e.SubmittedBy)
                .HasColumnType("int(11)")
                .HasColumnName("submitted_by");
            entity.Property(e => e.Summary)
                .HasColumnType("text")
                .HasColumnName("summary");
            entity.Property(e => e.TestAuditId)
                .HasColumnType("int(11)")
                .HasColumnName("test_audit_id");
            entity.Property(e => e.TestDate).HasColumnName("test_date");
            entity.Property(e => e.TestResult)
                .HasMaxLength(50)
                .HasColumnName("test_result");
        });

        modelBuilder.Entity<FrameworkControlTestResultsToRisk>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("framework_control_test_results_to_risks")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.RiskId)
                .HasColumnType("int(11)")
                .HasColumnName("risk_id");
            entity.Property(e => e.TestResultsId)
                .HasColumnType("int(11)")
                .HasColumnName("test_results_id");
        });

        modelBuilder.Entity<FrameworkControlToFramework>(entity =>
        {
            entity.HasKey(e => new { e.ControlId, e.FrameworkId })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

            entity
                .ToTable("framework_control_to_framework")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => new { e.FrameworkId, e.ControlId }, "framework_id");

            entity.Property(e => e.ControlId)
                .HasColumnType("int(11)")
                .HasColumnName("control_id");
            entity.Property(e => e.FrameworkId)
                .HasColumnType("int(11)")
                .HasColumnName("framework_id");
        });

        modelBuilder.Entity<FrameworkControlTypeMapping>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("framework_control_type_mappings")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.ControlId)
                .HasColumnType("int(11)")
                .HasColumnName("control_id");
            entity.Property(e => e.ControlTypeId)
                .HasColumnType("int(11)")
                .HasColumnName("control_type_id");
        });

        modelBuilder.Entity<Host>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("hosts")
                .UseCollation("utf8mb4_unicode_ci");

            entity.HasIndex(e => e.TeamId, "fk_host_team");

            entity.Property(e => e.Id).HasColumnType("int(11)");
            entity.Property(e => e.Comment).HasColumnType("text");
            entity.Property(e => e.Fqdn)
                .HasMaxLength(255)
                .HasColumnName("FQDN");
            entity.Property(e => e.HostName).HasMaxLength(255);
            entity.Property(e => e.Ip).HasMaxLength(255);
            entity.Property(e => e.LastVerificationDate).HasColumnType("datetime");
            entity.Property(e => e.MacAddress).HasMaxLength(255);
            entity.Property(e => e.Os)
                .HasMaxLength(255)
                .HasColumnName("OS");
            entity.Property(e => e.Properties).HasColumnType("text");
            entity.Property(e => e.RegistrationDate)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime");
            entity.Property(e => e.Source)
                .HasMaxLength(255)
                .HasDefaultValueSql("'manual'");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'1'")
                .HasColumnType("smallint(6)");
            entity.Property(e => e.TeamId).HasColumnType("int(11)");

            entity.HasOne(d => d.Team).WithMany(p => p.Hosts)
                .HasForeignKey(d => d.TeamId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_host_team");
        });

        modelBuilder.Entity<HostsService>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("hosts_services");

            entity.HasIndex(e => e.HostId, "fk_host");

            entity.HasIndex(e => e.Name, "idx_name");

            entity.HasIndex(e => e.Port, "idx_port");

            entity.HasIndex(e => e.Protocol, "idx_protocol");

            entity.Property(e => e.Id).HasColumnType("int(11)");
            entity.Property(e => e.HostId).HasColumnType("int(11)");
            entity.Property(e => e.Port).HasColumnType("int(11)");

            entity.HasOne(d => d.Host).WithMany(p => p.HostsServices)
                .HasForeignKey(d => d.HostId)
                .HasConstraintName("fk_host");
        });

        modelBuilder.Entity<Impact>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("impact")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.Value, "impact_index");

            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .HasColumnName("name");
            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
        });

        modelBuilder.Entity<Job>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("jobs");

            entity.HasIndex(e => e.OwnerId, "fk_user_job");

            entity.HasIndex(e => e.StartedAt, "idx_started");

            entity.HasIndex(e => e.Status, "idx_status");

            entity.HasIndex(e => e.LastUpdate, "idx_updated");

            entity.Property(e => e.Id).HasColumnType("int(11)");
            entity.Property(e => e.CancellationToken).HasColumnType("blob");
            entity.Property(e => e.LastUpdate).HasColumnType("datetime");
            entity.Property(e => e.OwnerId).HasColumnType("int(11)");
            entity.Property(e => e.Parameters).HasColumnType("blob");
            entity.Property(e => e.Progress).HasColumnType("int(11)");
            entity.Property(e => e.Result).HasColumnType("blob");
            entity.Property(e => e.StartedAt)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'1'")
                .HasColumnType("int(11)");
            entity.Property(e => e.Title).HasMaxLength(255);

            entity.HasOne(d => d.Owner).WithMany(p => p.Jobs)
                .HasForeignKey(d => d.OwnerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_user_job");
        });

        modelBuilder.Entity<Likelihood>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("likelihood")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.Value, "likelihood_index");

            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .HasColumnName("name");
            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
        });

        modelBuilder.Entity<Link>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("links");

            entity.HasIndex(e => e.ExpirationDate, "expiration_date_idx");

            entity.HasIndex(e => new { e.KeyHash, e.Type }, "key_type_idx").IsUnique();

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.CreationDate)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime")
                .HasColumnName("creation_date");
            entity.Property(e => e.Data)
                .HasColumnType("blob")
                .HasColumnName("data");
            entity.Property(e => e.ExpirationDate)
                .HasColumnType("datetime")
                .HasColumnName("expiration_date");
            entity.Property(e => e.KeyHash).HasColumnName("key_hash");
            entity.Property(e => e.Type).HasColumnName("type");
        });

        modelBuilder.Entity<Location>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("location")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("messages");

            entity.HasIndex(e => e.UserId, "fK_user_message");

            entity.HasIndex(e => e.CreatedAt, "idx_created_at");

            entity.HasIndex(e => e.Type, "idx_msg_type");

            entity.HasIndex(e => e.ReceivedAt, "idx_received_at");

            entity.HasIndex(e => e.Status, "idx_status");

            entity.Property(e => e.Id).HasColumnType("int(11)");
            entity.Property(e => e.ChatId).HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt)
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime");
            entity.Property(e => e.Message1)
                .HasColumnType("text")
                .HasColumnName("Message");
            entity.Property(e => e.ReceivedAt).HasColumnType("datetime");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'1'")
                .HasColumnType("int(11)");
            entity.Property(e => e.Type)
                .HasDefaultValueSql("'1'")
                .HasColumnType("int(11)");
            entity.Property(e => e.UserId).HasColumnType("int(11)");

            entity.HasOne(d => d.User).WithMany(p => p.Messages)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("fK_user_message");
        });

        modelBuilder.Entity<MgmtReview>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("mgmt_reviews")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.NextStep, "fk_next_step");

            entity.HasIndex(e => e.Review, "fk_review_type");

            entity.HasIndex(e => e.RiskId, "fk_risk");

            entity.HasIndex(e => e.Reviewer, "fw_rev");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.Comments)
                .HasColumnType("text")
                .HasColumnName("comments");
            entity.Property(e => e.NextReview)
                .HasDefaultValueSql("'0000-00-00'")
                .HasColumnName("next_review");
            entity.Property(e => e.NextStep)
                .HasColumnType("int(11)")
                .HasColumnName("next_step");
            entity.Property(e => e.Review)
                .HasColumnType("int(11)")
                .HasColumnName("review");
            entity.Property(e => e.Reviewer)
                .HasColumnType("int(11)")
                .HasColumnName("reviewer");
            entity.Property(e => e.RiskId)
                .HasColumnType("int(11)")
                .HasColumnName("risk_id");
            entity.Property(e => e.SubmissionDate)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("timestamp")
                .HasColumnName("submission_date");

            entity.HasOne(d => d.NextStepNavigation).WithMany(p => p.MgmtReviews)
                .HasForeignKey(d => d.NextStep)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_next_step");

            entity.HasOne(d => d.ReviewNavigation).WithMany(p => p.MgmtReviews)
                .HasForeignKey(d => d.Review)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_review_type");

            entity.HasOne(d => d.ReviewerNavigation).WithMany(p => p.MgmtReviews)
                .HasForeignKey(d => d.Reviewer)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fw_rev");

            entity.HasOne(d => d.Risk).WithMany(p => p.MgmtReviews)
                .HasForeignKey(d => d.RiskId)
                .HasConstraintName("fk_risk");
        });

        modelBuilder.Entity<Mitigation>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("mitigations")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.MitigationCost, "fk_mitigation_cost");

            entity.HasIndex(e => e.MitigationEffort, "fk_mitigation_effort");

            entity.HasIndex(e => e.MitigationOwner, "fk_mitigation_owner");

            entity.HasIndex(e => e.PlanningStrategy, "fk_planning_strategy");

            entity.HasIndex(e => e.RiskId, "fk_risks");

            entity.HasIndex(e => e.SubmittedBy, "fk_submitted_by");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.CurrentSolution)
                .HasColumnType("text")
                .HasColumnName("current_solution");
            entity.Property(e => e.LastUpdate)
                .HasDefaultValueSql("'0000-00-00 00:00:00'")
                .HasColumnType("timestamp")
                .HasColumnName("last_update");
            entity.Property(e => e.MitigationCost)
                .HasDefaultValueSql("'1'")
                .HasColumnType("int(11)")
                .HasColumnName("mitigation_cost");
            entity.Property(e => e.MitigationEffort)
                .HasDefaultValueSql("'1'")
                .HasColumnType("int(11)")
                .HasColumnName("mitigation_effort");
            entity.Property(e => e.MitigationOwner)
                .HasDefaultValueSql("'1'")
                .HasColumnType("int(11)")
                .HasColumnName("mitigation_owner");
            entity.Property(e => e.MitigationPercent)
                .HasColumnType("int(11)")
                .HasColumnName("mitigation_percent");
            entity.Property(e => e.PlanningDate).HasColumnName("planning_date");
            entity.Property(e => e.PlanningStrategy)
                .HasDefaultValueSql("'1'")
                .HasColumnType("int(11)")
                .HasColumnName("planning_strategy");
            entity.Property(e => e.RiskId)
                .HasColumnType("int(11)")
                .HasColumnName("risk_id");
            entity.Property(e => e.SecurityRecommendations)
                .HasColumnType("text")
                .HasColumnName("security_recommendations");
            entity.Property(e => e.SecurityRequirements)
                .HasColumnType("text")
                .HasColumnName("security_requirements");
            entity.Property(e => e.SubmissionDate)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("timestamp")
                .HasColumnName("submission_date");
            entity.Property(e => e.SubmittedBy)
                .HasDefaultValueSql("'1'")
                .HasColumnType("int(11)")
                .HasColumnName("submitted_by");

            entity.HasOne(d => d.MitigationCostNavigation).WithMany(p => p.Mitigations)
                .HasForeignKey(d => d.MitigationCost)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_mitigation_cost");

            entity.HasOne(d => d.MitigationEffortNavigation).WithMany(p => p.Mitigations)
                .HasForeignKey(d => d.MitigationEffort)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_mitigation_effort");

            entity.HasOne(d => d.MitigationOwnerNavigation).WithMany(p => p.MitigationMitigationOwnerNavigations)
                .HasForeignKey(d => d.MitigationOwner)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_mitigation_owner");

            entity.HasOne(d => d.PlanningStrategyNavigation).WithMany(p => p.Mitigations)
                .HasForeignKey(d => d.PlanningStrategy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_planning_strategy");

            entity.HasOne(d => d.Risk).WithMany(p => p.Mitigations)
                .HasForeignKey(d => d.RiskId)
                .HasConstraintName("fk_risks");

            entity.HasOne(d => d.SubmittedByNavigation).WithMany(p => p.MitigationSubmittedByNavigations)
                .HasForeignKey(d => d.SubmittedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_submitted_by");
        });

        modelBuilder.Entity<MitigationAcceptUser>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("mitigation_accept_users")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.RiskId, "mau_risk_id_idx");

            entity.HasIndex(e => new { e.RiskId, e.UserId }, "mau_risk_user_idx");

            entity.HasIndex(e => e.UserId, "mau_user_idx");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("datetime")
                .HasColumnName("created_at");
            entity.Property(e => e.RiskId)
                .HasColumnType("int(11)")
                .HasColumnName("risk_id");
            entity.Property(e => e.UserId)
                .HasColumnType("int(11)")
                .HasColumnName("user_id");
        });

        modelBuilder.Entity<MitigationCost>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity.ToTable("mitigation_cost");

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
        });

        modelBuilder.Entity<MitigationEffort>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("mitigation_effort")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Name)
                .HasMaxLength(20)
                .HasColumnName("name");
        });

        modelBuilder.Entity<MitigationToControl>(entity =>
        {
            entity.HasKey(e => new { e.MitigationId, e.ControlId })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

            entity
                .ToTable("mitigation_to_controls")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => new { e.ControlId, e.MitigationId }, "control_id");

            entity.HasIndex(e => e.ControlId, "mtg2ctrl_control_idx");

            entity.HasIndex(e => e.MitigationId, "mtg2ctrl_mtg_idx");

            entity.Property(e => e.MitigationId)
                .HasColumnType("int(11)")
                .HasColumnName("mitigation_id");
            entity.Property(e => e.ControlId)
                .HasColumnType("int(11)")
                .HasColumnName("control_id");
            entity.Property(e => e.ValidationDetails)
                .HasColumnType("mediumtext")
                .HasColumnName("validation_details");
            entity.Property(e => e.ValidationMitigationPercent)
                .HasDefaultValueSql("'0'")
                .HasColumnType("int(11)")
                .HasColumnName("validation_mitigation_percent");
            entity.Property(e => e.ValidationOwner)
                .HasDefaultValueSql("'0'")
                .HasColumnType("int(11)")
                .HasColumnName("validation_owner");
        });

        modelBuilder.Entity<MitigationToTeam>(entity =>
        {
            entity.HasKey(e => new { e.MitigationId, e.TeamId })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

            entity
                .ToTable("mitigation_to_team")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.MitigationId, "mtg2team_mtg_id");

            entity.HasIndex(e => e.TeamId, "mtg2team_team_id");

            entity.HasIndex(e => new { e.TeamId, e.MitigationId }, "team_id");

            entity.Property(e => e.MitigationId)
                .HasColumnType("int(11)")
                .HasColumnName("mitigation_id");
            entity.Property(e => e.TeamId)
                .HasColumnType("int(11)")
                .HasColumnName("team_id");
        });

        modelBuilder.Entity<NextStep>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("next_step")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .HasColumnName("name");
        });

        modelBuilder.Entity<NrAction>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("nr_actions");

            entity.HasIndex(e => e.UserId, "fx_action_user");

            entity.HasIndex(e => e.ObjectType, "idx_object_type").HasAnnotation("MySql:FullTextIndex", true);
            
            entity.HasIndex(e => e.Message, "idx_action_message").HasAnnotation("MySql:FullTextIndex", true);

            entity.Property(e => e.Id).HasColumnType("int(11)");
            entity.Property(e => e.DateTime)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime");
            entity.Property(e => e.Message).HasMaxLength(255);
            entity.Property(e => e.UserId).HasColumnType("int(11)");

            entity.HasOne(d => d.User).WithMany(p => p.NrActions)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fx_action_user");
        });

        modelBuilder.Entity<NrFile>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("nr_files")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.MitigationId, "idx_mitigation_id");

            entity.HasIndex(e => e.RiskId, "idx_risk_id");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.MitigationId)
                .HasColumnType("int(11)")
                .HasColumnName("mitigation_id");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
            entity.Property(e => e.RiskId)
                .HasDefaultValueSql("'0'")
                .HasColumnType("int(11)")
                .HasColumnName("risk_id");
            entity.Property(e => e.Size)
                .HasColumnType("int(11)")
                .HasColumnName("size");
            entity.Property(e => e.Timestamp)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("timestamp")
                .HasColumnName("timestamp");
            entity.Property(e => e.Type)
                .HasMaxLength(128)
                .HasColumnName("type");
            entity.Property(e => e.UniqueName)
                .HasMaxLength(128)
                .HasColumnName("unique_name")
                .UseCollation("utf8mb4_general_ci")
                .HasCharSet("utf8mb4");
            entity.Property(e => e.User)
                .HasColumnType("int(11)")
                .HasColumnName("user");
            entity.Property(e => e.ViewType)
                .HasDefaultValueSql("'1'")
                .HasColumnType("int(11)")
                .HasColumnName("view_type");
        });

        /*modelBuilder.Entity<IncidentToIncidentResponsePlan>(entity =>
        {
            entity
                .ToTable("IncidentToIncidentResponsePlan")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");
            
            entity.HasKey(e => new { e.IncidentId, e.IncidentResponsePlanId })
                .HasName("PRIMARY");
            
            entity.HasIndex( e => new { e.IncidentId, e.IncidentResponsePlanId} , "idx_incident_id");
            
            entity.Property(e => e.IncidentId)
                .HasColumnType("int(11)");
            
            entity.Property(e => e.IncidentResponsePlanId)
                .HasColumnType("int(11)");
            
            
        });*/

        modelBuilder.Entity<Incident>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            
            entity
                .ToTable("Incidents")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");
            
            entity.Property(e => e.Description)
                .HasColumnType("text");
            
            entity.Property(e => e.Name)
                .HasColumnType("varchar(250)");
            
            entity.HasIndex(e => e.Name, "idx_inc_name").HasAnnotation("MySql:FullTextIndex", true);
            
            entity.Property(e => e.CreationDate)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime");
            
            entity.Property(e => e.CreatedById)
                .HasColumnType("int(11)");
            
            entity.HasOne(e => e.CreatedBy)
                .WithMany(u => u.IncidentsCreated)
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_inc_created_by");
            
            entity.Property(e => e.LastUpdate)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime");
            
            entity.Property(e => e.UpdatedById)
                .HasColumnType("int(11)");
            
            entity.HasOne(e => e.UpdatedBy)
                .WithMany(u => u.IncidentsUpdated)
                .HasForeignKey(e => e.UpdatedById)
                .HasConstraintName("fk_inc_updated_by");
            
            entity.Property(e => e.Status)
                .HasColumnType("int(6)");
            
            entity.Property(e => e.Report)
                .HasColumnType("text");
            entity.HasIndex(e => e.Name, "idx_inc_repo").HasAnnotation("MySql:FullTextIndex", true);
            
            entity.Property(e => e.Notes)
                .HasColumnType("text");
            
            entity.Property(e => e.Impact)
                .HasColumnType("text");
            
            entity.Property(e => e.Cause)
                .HasColumnType("text");
            
            entity.Property(e => e.Resolution)
                .HasColumnType("text");
            
            entity.Property(e => e.Duration)
                .HasConversion(new TimeSpanToTicksConverter());
            
            entity.Property(e => e.StartDate)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime");
            
            entity.HasMany(e => e.Attachments)
                .WithOne(t => t.Incident)
                .HasForeignKey(t => t.IncidentId)
                .HasConstraintName("fk_inc_attachments");
            
            entity.HasMany(e => e.Actions)
                .WithOne(t => t.Incident)
                .HasForeignKey(t => t.IncidentId)
                .HasConstraintName("fk_inc_actions");

            entity.HasMany(e => e.IncidentResponsePlansActivated)
                .WithMany(e => e.ActivatedBy)
                .UsingEntity<IncidentToIncidentResponsePlan>(
                    l=> l.HasOne<IncidentResponsePlan>().WithMany().HasForeignKey(e => e.IncidentResponsePlanId),
                    r=> r.HasOne<Incident>().WithMany().HasForeignKey(e => e.IncidentId)
                );

        });

        modelBuilder.Entity<IncidentResponsePlan>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            
            entity
                .ToTable("IncidentResponsePlans")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");
            
            entity.Property(o => o.Id).IsRequired().ValueGeneratedOnAdd();

            entity.Property(e => e.Description)
                .HasColumnType("text");
            
            entity.Property(e => e.Name)
                .HasColumnType("varchar(255)");
            
            entity.Property(e => e.Status)
                .HasColumnType("int(11)")
                .HasDefaultValueSql("0");
            
            entity.HasIndex(e => e.Status, "idx_irpt_status");
            
            entity.HasIndex(e => e.Name, "idx_irp_name").HasAnnotation("MySql:FullTextIndex", true);
            
            entity.Property(e => e.CreationDate)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime");
            
            entity.Property(e => e.LastUpdate)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime");

            entity.Property(e => e.LastTestDate)
                .HasColumnType("datetime");
            
            entity.Property(e => e.LastReviewDate)
                .HasColumnType("datetime");
            
            entity.Property(e => e.LastExerciseDate)
                .HasColumnType("datetime");

            entity.Property(e => e.ApprovalDate)
                .HasColumnType("datetime");
            
            entity.Property(e => e.CreatedById)
                .HasColumnType("int(11)");
            
            entity.Property(e => e.HasBeenApproved)
                .HasColumnType("tinyint(1)")
                .HasDefaultValueSql("0");
            
            entity.Property(e => e.HasBeenExercised)
                .HasColumnType("tinyint(1)")
                .HasDefaultValueSql("0");

            entity.Property(e => e.HasBeenReviewed)
                .HasColumnType("tinyint(1)")
                .HasDefaultValueSql("0");

            entity.Property(e => e.HasBeenTested)
                .HasColumnType("tinyint(1)")
                .HasDefaultValueSql("0");

            entity.Property(e => e.HasBeenUpdated)
                .HasColumnType("tinyint(1)")
                .HasDefaultValueSql("0");
            
            entity.Property(e => e.UpdatedById)
                .HasColumnType("int(11)");
            
            
            entity.Property(e => e.LastTestedById)
                .HasColumnType("int(11)");
            
            entity.HasOne(e => e.CreatedBy)
                .WithMany(u => u.IncidentResponsePlans)
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_irp_created_by");
            
            entity.HasOne(e => e.UpdatedBy)
                .WithMany(u => u.IncidentResponsePlansUpdated)
                .HasForeignKey(e => e.UpdatedById)
                .HasConstraintName("fk_irp_updated_by");

            entity.HasOne(e => e.LastExercisedBy)
                .WithMany(ent => ent.IncidentResponsePlansLastExercised)
                .HasForeignKey(e => e.LastExercisedById)
                .HasConstraintName("fk_irp_last_exercised_by");

            entity.HasOne(e => e.LastTestedBy)
                .WithMany(ent => ent.IncidentResponsePlansLastTested)
                .HasForeignKey(e => e.LastTestedById)
                .HasConstraintName("fk_irp_last_tested_by");
            
            entity.HasOne(e => e.LastReviewedBy)
                .WithMany(ent => ent.IncidentResponsePlansLastReviewed)
                .HasForeignKey(e => e.LastReviewedById)
                .HasConstraintName("fk_irp_last_reviewed_by");
            
            entity.HasMany(e => e.Tasks)
                .WithOne(t => t.Plan)
                .HasForeignKey(t => t.PlanId)
                .HasConstraintName("fk_irp_task");
            
            entity.HasMany(e => e.Executions)
                .WithOne(t => t.Plan)
                .HasForeignKey(t => t.PlanId)
                .HasConstraintName("fk_irp_executions");
            
            entity.HasMany(e => e.Attachments)
                .WithOne(t => t.IncidentResponsePlan)
                .HasForeignKey(t => t.IncidentResponsePlanId)
                .HasConstraintName("fk_irp_attachments");

        });

        modelBuilder.Entity<IncidentResponsePlanExecution>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("IncidentResponsePlanExecutions")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");
            
            entity.Property(e => e.Status)
                .HasColumnType("int(11)")
                .HasDefaultValueSql("0");
            
            entity.HasIndex(e => e.Status, "idx_irpt_status");
            
            entity.Property(e => e.Duration)
                .HasConversion(new TimeSpanToTicksConverter());
            
            entity.Property(e => e.CreationDate)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime");
            
            entity.Property(e => e.ExecutionDate)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime");
            
            entity.Property(e => e.LastUpdateDate)
                .HasColumnType("datetime");
            
            entity.Property(e => e.Notes)
                .HasColumnType("text");
            
            entity.Property(e => e.ExecutionTrigger)
                .HasColumnType("text");
            
            entity.Property(e => e.ExecutionResult)
                .HasColumnType("text");
            
            entity.Property(e => e.IsExercise)
                .HasColumnType("tinyint(1)")
                .HasDefaultValueSql("0");
            
            entity.Property(e => e.IsTest)
                .HasColumnType("tinyint(1)")
                .HasDefaultValueSql("0");
            
            entity.HasOne(i => i.Plan)
                .WithMany(p => p.Executions)
                .HasForeignKey(i => i.PlanId)
                .HasConstraintName("fk_irp_executions_plan");
            
            entity.HasOne(i => i.ExecutedBy)
                .WithMany(u => u.IncidentResponsePlanExecutions)
                .HasForeignKey(i => i.ExecutedById)
                .HasConstraintName("fk_irp_executions_user");
            
            entity.Property(e => e.CreatedById)
                .HasColumnType("int(11)");
            
            entity.HasOne(e => e.CreatedBy)
                .WithMany(u => u.IncidentResponsePlanExecutions)
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_irp_executions_created_by");
            
            entity.Property(e => e.LastUpdatedById)
                .HasColumnType("int(11)");
            
            entity.HasOne(e => e.LastUpdatedBy)
                .WithMany(u => u.IncidentResponsePlanExecutionsLastUpdated)
                .HasForeignKey(e => e.LastUpdatedById)
                .HasConstraintName("fk_irp_executions_updated_by");

            entity.HasMany(i => i.Attachments)
                .WithOne(a => a.IncidentResponsePlanExecution)
                .HasForeignKey(a => a.IncidentResponsePlanExecutionId)
                .HasConstraintName("fk_irp_executions_attachments");
            
        });

        modelBuilder.Entity<IncidentResponsePlanTask>(entity =>
        {
            
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("IncidentResponsePlanTasks")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");
            
            entity.HasOne(t => t.Plan)
                .WithMany(p => p.Tasks)
                .HasForeignKey(t => t.PlanId)
                .HasConstraintName("fk_irp_task_plan");
            
            entity.Property(e => e.Description)
                .HasColumnType("text");
            
            entity.Property(e => e.CreationDate)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime");
            
            entity.Property(e => e.CreatedById)
                .HasColumnType("int(11)");
            
            entity.HasOne(e => e.CreatedBy)
                .WithMany(u => u.IncidentResponseTasksCreated)
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_irpt_created_by");
            
            entity.Property(e => e.UpdatedById)
                .HasColumnType("int(11)");
            
            entity.HasOne(e => e.UpdatedBy)
                .WithMany(u => u.IncidentResponseTasksUpdated)
                .HasForeignKey(e => e.UpdatedById)
                .HasConstraintName("fk_irpt_updated_by");
            
            entity.Property(e => e.Status)
                .HasColumnType("int(11)")
                .HasDefaultValueSql("0");
            
            entity.HasIndex(e => e.Status, "idx_irpt_status");
            
            entity.Property(e => e.Notes)
                .HasColumnType("text");
            
            entity.Property(e => e.HasBeenTested)
                .HasColumnType("tinyint(1)")
                .HasDefaultValueSql("0");
            
            entity.Property(e => e.PlanId)
                .HasColumnType("int(11)");
            
            entity.Property(e => e.ExecutionOrder)
                .HasDefaultValueSql("1")
                .HasColumnType("int(11)");
            
            entity.Property(e => e.LastTestDate)
                .HasColumnType("datetime");
            
            entity.Property(e => e.IsSequential)
                .HasColumnType("tinyint(1)")
                .HasDefaultValueSql("0");
            
            entity.Property(e => e.IsOptional)
                .HasColumnType("tinyint(1)")
                .HasDefaultValueSql("0");
            
            entity.Property(e => e.IsParallel)
                .HasColumnType("tinyint(1)")
                .HasDefaultValueSql("0");
            
            entity.Property(e => e.Priority)
                .HasColumnType("int(11)")
                .HasDefaultValueSql("1");
            
            entity.HasOne( irp => irp.LastTestedBy)
                .WithMany( e => e.IncidentResponsePlanTasksLastTested)
                .HasForeignKey( irp => irp.LastTestedById)
                .HasConstraintName("fk_irp_task_last_tested_by");


            entity.Property(e => e.AssignedToId)
                .HasColumnType("int(11)");
            
            entity.HasOne( irpt => irpt.AssignedTo)
                .WithMany( e => e.IncidentResponsePlanTasks)
                .HasForeignKey( irpt => irpt.AssignedToId)
                .HasConstraintName("fk_irpt_task_assigned_to");
            
            entity.HasMany(t => t.Attachments)
                .WithOne(a => a.IncidentResponsePlanTask)
                .HasForeignKey(a => a.IncidentResponsePlanTaskId)
                .HasConstraintName("fk_irp_task_attachments");
            
            entity.HasMany(t => t.Executions)
                .WithOne(e => e.Task)
                .HasForeignKey(e => e.TaskId)
                .HasConstraintName("fk_irp_task_executions");
            
            /*entity.HasMany(irt => irt.AssignedTo).WithMany(u => u.IncidentResponsePlanTasks)
                .UsingEntity<Dictionary<string, object>>(
                    "IncidentResponsePlanTaskToUser",
                    r => r.HasOne<Entity>().WithMany()
                        .HasForeignKey("EntityId")
                        .HasConstraintName("fk_irt_entity_irt"),
                    l => l.HasOne<IncidentResponsePlanTask>().WithMany()
                        .HasForeignKey("IncidentResponsePlanTaskId")
                        .HasConstraintName("fk_irt_irt_entity"),
                    j =>
                    {
                        j.HasKey("IncidentResponsePlanTaskId", "EntityId")
                            .HasName("PRIMARY")
                            .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });
                        j
                            .ToTable("IncidentResponsePlanTaskToEntity")
                            .HasCharSet("utf8mb3")
                            .UseCollation("utf8mb3_general_ci");
                        j.HasIndex(new[] { "EntityId", "IncidentResponsePlanTaskId" }, "irt_id");
                        j.IndexerProperty<int>("IncidentResponsePlanTaskId")
                            .HasColumnType("int(11)");
                        j.IndexerProperty<int>("EntityId")
                            .HasColumnType("int(11)");
                    });*/
            
        });

        modelBuilder.Entity<IncidentResponsePlanTaskExecution>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            
            entity.Property(e => e.PlanExecutionId)
                .HasColumnType("int(11)");
            
            entity.Property(e => e.TaskId)
                .HasColumnType("int(11)");
            
            entity.Property(e => e.Duration)
                .HasConversion(new TimeSpanToTicksConverter());
            
            entity.Property(e => e.ExecutionDate)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime");
            
            entity.Property(e => e.Status)
                .HasColumnType("int(11)")
                .HasDefaultValueSql("0");

            entity.HasIndex(e => e.Status, "idx_irpt_exec_status");
            
            entity.Property(e => e.Notes)
                .HasColumnType("text");
            
            entity.Property(e => e.IsExercise)
                .HasColumnType("tinyint(1)")
                .HasDefaultValueSql("0");
            
            entity.Property(e => e.IsTest)
                .HasColumnType("tinyint(1)")
                .HasDefaultValueSql("0");
            
            entity.Property(e => e.CreatedById)
                .HasColumnType("int(11)");
            
            entity.Property(e => e.LastUpdatedById)
                .HasColumnType("int(11)");
            
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime");
            
            entity.Property(e => e.LastUpdatedAt)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime");
            
            entity.HasOne(e => e.PlanExecution)
                .WithMany(p => p.TasksExecuted)
                .HasForeignKey( e=> e.PlanExecutionId)
                .HasConstraintName("fk_irpt_executions_plan");

            entity.HasOne(e => e.Task)
                .WithMany(t => t.Executions)
                .HasForeignKey(e => e.TaskId)
                .HasConstraintName("fk_irpt_executions_task");
            
            entity.HasOne(e => e.CreatedBy)
                .WithMany(u => u.IncidentResponsePlanTaskExecutions)
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_irpt_executions_created_by");
            
            entity.HasOne(e => e.LastUpdatedBy)
                .WithMany(u => u.IncidentResponsePlanTaskExecutionsLastUpdated)
                .HasForeignKey(e => e.LastUpdatedById)
                .HasConstraintName("fk_irpt_executions_last_updated_by");
            
            entity.HasOne(e => e.ExecutedBy)
                .WithMany(ent => ent.IncidentResponsePlanTaskExecutions)
                .HasForeignKey( e => e.ExecutedById)
                .HasConstraintName("fk_irpt_executions_entity");
            
            entity.HasMany(e => e.Attachments)
                .WithOne(f => f.IncidentResponsePlanTaskExecution)
                .HasForeignKey( f=> f.IncidentResponsePlanTaskExecutionId)
                .HasConstraintName("fk_irpt_executions_attachments");


        });

        modelBuilder.Entity<PendingRisk>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("pending_risks")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.AffectedAssets)
                .HasColumnType("text")
                .HasColumnName("affected_assets");
            entity.Property(e => e.AssessmentAnswerId)
                .HasColumnType("int(11)")
                .HasColumnName("assessment_answer_id");
            entity.Property(e => e.AssessmentId)
                .HasColumnType("int(11)")
                .HasColumnName("assessment_id");
            entity.Property(e => e.Comment)
                .HasColumnType("text")
                .HasColumnName("comment");
            entity.Property(e => e.Owner)
                .HasColumnType("int(11)")
                .HasColumnName("owner");
            entity.Property(e => e.Score).HasColumnName("score");
            entity.Property(e => e.Subject)
                .HasColumnType("blob")
                .HasColumnName("subject");
            entity.Property(e => e.SubmissionDate)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("timestamp")
                .HasColumnName("submission_date");
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("permissions")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.Key, "key").IsUnique();

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.Description)
                .HasColumnType("blob")
                .HasColumnName("description");
            entity.Property(e => e.Key)
                .HasMaxLength(100)
                .HasColumnName("key");
            entity.Property(e => e.Name)
                .HasMaxLength(200)
                .HasColumnName("name");
            entity.Property(e => e.Order)
                .HasColumnType("int(11)")
                .HasColumnName("order");

            entity.HasMany(d => d.Users).WithMany(p => p.Permissions)
                .UsingEntity<Dictionary<string, object>>(
                    "PermissionToUser",
                    r => r.HasOne<User>().WithMany()
                        .HasForeignKey("UserId")
                        .HasConstraintName("fk_user_perm"),
                    l => l.HasOne<Permission>().WithMany()
                        .HasForeignKey("PermissionId")
                        .HasConstraintName("fk_perm_user"),
                    j =>
                    {
                        j.HasKey("PermissionId", "UserId")
                            .HasName("PRIMARY")
                            .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });
                        j
                            .ToTable("permission_to_user")
                            .HasCharSet("utf8mb3")
                            .UseCollation("utf8mb3_general_ci");
                        j.HasIndex(new[] { "UserId", "PermissionId" }, "user_id");
                        j.IndexerProperty<int>("PermissionId")
                            .HasColumnType("int(11)")
                            .HasColumnName("permission_id");
                        j.IndexerProperty<int>("UserId")
                            .HasColumnType("int(11)")
                            .HasColumnName("user_id");
                    });
        });

        modelBuilder.Entity<PermissionGroup>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("permission_groups")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.Name, "name").IsUnique();

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.Description)
                .HasColumnType("blob")
                .HasColumnName("description");
            entity.Property(e => e.Name)
                .HasMaxLength(200)
                .HasColumnName("name");
            entity.Property(e => e.Order)
                .HasColumnType("int(11)")
                .HasColumnName("order");
        });

        modelBuilder.Entity<PermissionToPermissionGroup>(entity =>
        {
            entity.HasKey(e => new { e.PermissionId, e.PermissionGroupId })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

            entity
                .ToTable("permission_to_permission_group")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => new { e.PermissionGroupId, e.PermissionId }, "permission_group_id");

            entity.Property(e => e.PermissionId)
                .HasColumnType("int(11)")
                .HasColumnName("permission_id");
            entity.Property(e => e.PermissionGroupId)
                .HasColumnType("int(11)")
                .HasColumnName("permission_group_id");
        });

        modelBuilder.Entity<PlanningStrategy>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("planning_strategy")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Name)
                .HasMaxLength(20)
                .HasColumnName("name");
        });

        modelBuilder.Entity<QuestionnairePendingRisk>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("questionnaire_pending_risks")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.Asset)
                .HasMaxLength(200)
                .HasColumnName("asset");
            entity.Property(e => e.Comment)
                .HasMaxLength(500)
                .HasColumnName("comment");
            entity.Property(e => e.Owner)
                .HasColumnType("int(11)")
                .HasColumnName("owner");
            entity.Property(e => e.QuestionnaireScoringId)
                .HasColumnType("int(11)")
                .HasColumnName("questionnaire_scoring_id");
            entity.Property(e => e.QuestionnaireTrackingId)
                .HasColumnType("int(11)")
                .HasColumnName("questionnaire_tracking_id");
            entity.Property(e => e.Subject)
                .HasColumnType("blob")
                .HasColumnName("subject");
            entity.Property(e => e.SubmissionDate)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("timestamp")
                .HasColumnName("submission_date");
        });

        modelBuilder.Entity<Regulation>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("regulation")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Report>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("reports");

            entity.HasIndex(e => e.CreatorId, "fk_creator_id");

            entity.HasIndex(e => e.FileId, "fk_file_id");

            entity.HasIndex(e => e.Name, "idx_name");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.CreationDate)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime")
                .HasColumnName("creationDate");
            entity.Property(e => e.CreatorId)
                .HasColumnType("int(11)")
                .HasColumnName("creatorId");
            entity.Property(e => e.FileId)
                .HasColumnType("int(11)")
                .HasColumnName("fileId");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Parameters)
                .HasColumnType("text")
                .HasColumnName("parameters");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'0000000000'")
                .HasColumnType("int(10) unsigned zerofill")
                .HasColumnName("status");
            entity.Property(e => e.Type)
                .HasDefaultValueSql("'1'")
                .HasColumnType("int(11)")
                .HasColumnName("type");

            entity.HasOne(d => d.Creator).WithMany(p => p.Reports)
                .HasForeignKey(d => d.CreatorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_creator_id");

            entity.HasOne(d => d.File).WithMany(p => p.Reports)
                .HasForeignKey(d => d.FileId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_file_id");
        });

        modelBuilder.Entity<ResidualRiskScoringHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("residual_risk_scoring_history")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.RiskId, "risk_id");

            entity.HasIndex(e => e.LastUpdate, "rrsh_last_update_idx");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.LastUpdate)
                .HasColumnType("datetime")
                .HasColumnName("last_update");
            entity.Property(e => e.ResidualRisk).HasColumnName("residual_risk");
            entity.Property(e => e.RiskId)
                .HasColumnType("int(11)")
                .HasColumnName("risk_id");
        });

        modelBuilder.Entity<Review>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("review")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .HasColumnName("name");
        });

        modelBuilder.Entity<ReviewLevel>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("review_levels")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.Name)
                .HasMaxLength(20)
                .HasColumnName("name");
            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
        });

        modelBuilder.Entity<Risk>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("risks")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.Category, "category");

            entity.HasIndex(e => e.CloseId, "close_id");

            entity.HasIndex(e => e.MitigationId, "fk_risk_mitigation");

            entity.HasIndex(e => e.Manager, "manager");

            entity.HasIndex(e => e.Owner, "owner");

            entity.HasIndex(e => e.ProjectId, "project_id");

            entity.HasIndex(e => e.Regulation, "regulation");

            entity.HasIndex(e => e.Source, "source");

            entity.HasIndex(e => e.Status, "status");

            entity.HasIndex(e => e.SubmittedBy, "submitted_by");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.Assessment).HasColumnName("assessment");
            entity.Property(e => e.Category)
                .HasColumnType("int(11)")
                .HasColumnName("category");
            entity.Property(e => e.CloseId)
                .HasColumnType("int(11)")
                .HasColumnName("close_id");
            entity.Property(e => e.ControlNumber)
                .HasMaxLength(20)
                .HasColumnName("control_number");
            entity.Property(e => e.LastUpdate)
                .HasDefaultValueSql("'0000-00-00 00:00:00'")
                .HasColumnType("timestamp")
                .HasColumnName("last_update");
            entity.Property(e => e.Manager)
                .HasColumnType("int(11)")
                .HasColumnName("manager");
            entity.Property(e => e.MitigationId)
                .HasColumnType("int(11)")
                .HasColumnName("mitigation_id");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.Owner)
                .HasColumnType("int(11)")
                .HasColumnName("owner");
            entity.Property(e => e.ProjectId)
                .HasColumnType("int(11)")
                .HasColumnName("project_id");
            entity.Property(e => e.ReferenceId)
                .HasMaxLength(20)
                .HasDefaultValueSql("''")
                .HasColumnName("reference_id");
            entity.Property(e => e.Regulation)
                .HasColumnType("int(11)")
                .HasColumnName("regulation");
            entity.Property(e => e.RiskCatalogMapping)
                .HasMaxLength(255)
                .HasColumnName("risk_catalog_mapping");
            entity.Property(e => e.Source)
                .HasColumnType("int(11)")
                .HasColumnName("source");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasColumnName("status");
            entity.Property(e => e.Subject).HasColumnName("subject");
            entity.Property(e => e.SubmissionDate)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("timestamp")
                .HasColumnName("submission_date");
            entity.Property(e => e.SubmittedBy)
                .HasDefaultValueSql("'1'")
                .HasColumnType("int(11)")
                .HasColumnName("submitted_by");
            entity.Property(e => e.TemplateGroupId)
                .HasDefaultValueSql("'1'")
                .HasColumnType("int(11)")
                .HasColumnName("template_group_id");
            entity.Property(e => e.ThreatCatalogMapping)
                .HasMaxLength(255)
                .HasColumnName("threat_catalog_mapping");

            entity.HasOne(d => d.CategoryNavigation).WithMany(p => p.Risks)
                .HasForeignKey(d => d.Category)
                .HasConstraintName("fk_risk_category");

            entity.HasOne(d => d.Mitigation).WithMany(p => p.Risks)
                .HasForeignKey(d => d.MitigationId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_risk_mitigation");

            entity.HasOne(d => d.SourceNavigation).WithMany(p => p.Risks)
                .HasForeignKey(d => d.Source)
                .HasConstraintName("fk_risk_source");

            entity.HasMany(d => d.Entities).WithMany(p => p.Risks)
                .UsingEntity<Dictionary<string, object>>(
                    "RiskToEntity",
                    r => r.HasOne<Entity>().WithMany()
                        .HasForeignKey("EntityId")
                        .HasConstraintName("fk_entity_id"),
                    l => l.HasOne<Risk>().WithMany()
                        .HasForeignKey("RiskId")
                        .HasConstraintName("fk_risk_id"),
                    j =>
                    {
                        j.HasKey("RiskId", "EntityId")
                            .HasName("PRIMARY")
                            .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });
                        j.ToTable("risk_to_entity");
                        j.HasIndex(new[] { "EntityId" }, "fk_entity_id");
                        j.IndexerProperty<int>("RiskId")
                            .HasColumnType("int(11)")
                            .HasColumnName("risk_id");
                        j.IndexerProperty<int>("EntityId")
                            .HasColumnType("int(11)")
                            .HasColumnName("entity_id");
                    });

            entity.HasMany(r => r.RiskCatalogs).WithMany(c => c.Risks)
                .UsingEntity<Dictionary<string, object>>("RisksToCatalog",
                    r => r.HasOne<RiskCatalog>().WithMany()
                        .HasForeignKey("RiskCatalogId")
                        .HasConstraintName("fk_risk_rcatalog"),
                    l => l.HasOne<Risk>().WithMany()
                        .HasForeignKey("RiskId")
                        .HasConstraintName("fk_riskcatlog_risk_2"),
                    j =>
                    {
                        j.HasKey("RiskId", "RiskCatalogId")
                            .HasName("PRIMARY")
                            .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });
                    }
                );

            entity.HasMany(d => d.Vulnerabilities).WithMany(p => p.Risks)
                .UsingEntity<Dictionary<string, object>>(
                    "RisksToVulnerability",
                    r => r.HasOne<Vulnerability>().WithMany()
                        .HasForeignKey("VulnerabilityId")
                        .HasConstraintName("fk_rv_v"),
                    l => l.HasOne<Risk>().WithMany()
                        .HasForeignKey("RiskId")
                        .HasConstraintName("fk_rv_r"),
                    j =>
                    {
                        j.HasKey("RiskId", "VulnerabilityId")
                            .HasName("PRIMARY")
                            .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });
                        j
                            .ToTable("risks_to_vulnerabilities")
                            .UseCollation("utf8mb4_unicode_ci");
                        j.HasIndex(new[] { "VulnerabilityId" }, "fk_rv_v");
                        j.IndexerProperty<int>("RiskId")
                            .HasColumnType("int(11)")
                            .HasColumnName("risk_id");
                        j.IndexerProperty<int>("VulnerabilityId")
                            .HasColumnType("int(11)")
                            .HasColumnName("vulnerability_id");
                    });
        });

        modelBuilder.Entity<RiskCatalog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("risk_catalog")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.Description)
                .HasColumnType("text")
                .HasColumnName("description");
            entity.Property(e => e.Function)
                .HasColumnType("int(11)")
                .HasColumnName("function");
            entity.Property(e => e.Grouping)
                .HasColumnType("int(11)")
                .HasColumnName("grouping");
            entity.Property(e => e.Name)
                .HasMaxLength(1000)
                .HasColumnName("name");
            entity.Property(e => e.Number)
                .HasMaxLength(20)
                .HasColumnName("number");
            entity.Property(e => e.Order)
                .HasColumnType("int(11)")
                .HasColumnName("order");
        });

        modelBuilder.Entity<RiskFunction>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("risk_function")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .HasColumnName("name");
        });

        modelBuilder.Entity<RiskGrouping>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("risk_grouping")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Default).HasColumnName("default");
            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .HasColumnName("name");
            entity.Property(e => e.Order)
                .HasColumnType("int(11)")
                .HasColumnName("order");
        });

        modelBuilder.Entity<RiskLevel>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("risk_levels")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.Name, "risk_levels_name_idx");

            entity.HasIndex(e => e.Value, "risk_levels_value_idx");

            entity.Property(e => e.Color)
                .HasMaxLength(20)
                .HasColumnName("color");
            entity.Property(e => e.DisplayName)
                .HasMaxLength(20)
                .HasColumnName("display_name");
            entity.Property(e => e.Name)
                .HasMaxLength(20)
                .HasColumnName("name");
            entity.Property(e => e.Value)
                .HasPrecision(3, 1)
                .HasColumnName("value");
        });

        modelBuilder.Entity<RiskScoring>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("risk_scoring")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.CalculatedRisk, "calculated_risk");

            entity.HasIndex(e => e.Id, "id").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.CalculatedRisk).HasColumnName("calculated_risk");
            entity.Property(e => e.ClassicImpact)
                .HasDefaultValueSql("'5'")
                .HasColumnName("CLASSIC_impact");
            entity.Property(e => e.ClassicLikelihood)
                .HasDefaultValueSql("'5'")
                .HasColumnName("CLASSIC_likelihood");
            entity.Property(e => e.ContributingScore).HasColumnName("contributing_score");
            entity.Property(e => e.Custom).HasDefaultValueSql("'10'");
            entity.Property(e => e.ScoringMethod)
                .HasColumnType("int(11)")
                .HasColumnName("scoring_method");
        });

        modelBuilder.Entity<RiskScoringContributingImpact>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("risk_scoring_contributing_impacts")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.ContributingRiskId, "contributing_risk_id");

            entity.HasIndex(e => e.RiskScoringId, "risk_scoring_id");

            entity.HasIndex(e => e.Impact, "rsci_impact_idx");

            entity.HasIndex(e => new { e.RiskScoringId, e.ContributingRiskId }, "rsci_index");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.ContributingRiskId)
                .HasColumnType("int(11)")
                .HasColumnName("contributing_risk_id");
            entity.Property(e => e.Impact)
                .HasColumnType("int(11)")
                .HasColumnName("impact");
            entity.Property(e => e.RiskScoringId)
                .HasColumnType("int(11)")
                .HasColumnName("risk_scoring_id");
        });

        modelBuilder.Entity<RiskScoringHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("risk_scoring_history")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.RiskId, "risk_id");

            entity.HasIndex(e => e.LastUpdate, "rsh_last_update_idx");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.CalculatedRisk).HasColumnName("calculated_risk");
            entity.Property(e => e.LastUpdate)
                .HasColumnType("datetime")
                .HasColumnName("last_update");
            entity.Property(e => e.RiskId)
                .HasColumnType("int(11)")
                .HasColumnName("risk_id");
        });

        modelBuilder.Entity<RiskToAdditionalStakeholder>(entity =>
        {
            entity.HasKey(e => new { e.RiskId, e.UserId })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

            entity
                .ToTable("risk_to_additional_stakeholder")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => new { e.UserId, e.RiskId }, "user_id");

            entity.Property(e => e.RiskId)
                .HasColumnType("int(11)")
                .HasColumnName("risk_id");
            entity.Property(e => e.UserId)
                .HasColumnType("int(11)")
                .HasColumnName("user_id");
        });

        modelBuilder.Entity<RiskToLocation>(entity =>
        {
            entity.HasKey(e => new { e.RiskId, e.LocationId })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

            entity
                .ToTable("risk_to_location")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => new { e.LocationId, e.RiskId }, "location_id");

            entity.Property(e => e.RiskId)
                .HasColumnType("int(11)")
                .HasColumnName("risk_id");
            entity.Property(e => e.LocationId)
                .HasColumnType("int(11)")
                .HasColumnName("location_id");
        });

        modelBuilder.Entity<RiskToTeam>(entity =>
        {
            entity.HasKey(e => new { e.RiskId, e.TeamId })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

            entity
                .ToTable("risk_to_team")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.RiskId, "risk2team_risk_id");

            entity.HasIndex(e => e.TeamId, "risk2team_team_id");

            entity.HasIndex(e => new { e.TeamId, e.RiskId }, "team_id");

            entity.Property(e => e.RiskId)
                .HasColumnType("int(11)")
                .HasColumnName("risk_id");
            entity.Property(e => e.TeamId)
                .HasColumnType("int(11)")
                .HasColumnName("team_id");
        });

        modelBuilder.Entity<RiskToTechnology>(entity =>
        {
            entity.HasKey(e => new { e.RiskId, e.TechnologyId })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

            entity
                .ToTable("risk_to_technology")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => new { e.TechnologyId, e.RiskId }, "technology_id");

            entity.Property(e => e.RiskId)
                .HasColumnType("int(11)")
                .HasColumnName("risk_id");
            entity.Property(e => e.TechnologyId)
                .HasColumnType("int(11)")
                .HasColumnName("technology_id");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("role")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.Default, "default").IsUnique();

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Admin).HasColumnName("admin");
            entity.Property(e => e.Default).HasColumnName("default");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");

            entity.HasMany(d => d.Permissions).WithMany(p => p.Roles)
                .UsingEntity<Dictionary<string, object>>(
                    "RoleResponsibility",
                    r => r.HasOne<Permission>().WithMany()
                        .HasForeignKey("PermissionId")
                        .HasConstraintName("fk_role_perm_id"),
                    l => l.HasOne<Role>().WithMany()
                        .HasForeignKey("RoleId")
                        .HasConstraintName("fk_role_p_id"),
                    j =>
                    {
                        j.HasKey("RoleId", "PermissionId")
                            .HasName("PRIMARY")
                            .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });
                        j
                            .ToTable("role_responsibilities")
                            .HasCharSet("utf8mb3")
                            .UseCollation("utf8mb3_general_ci");
                        j.HasIndex(new[] { "PermissionId", "RoleId" }, "permission_id");
                        j.IndexerProperty<int>("RoleId")
                            .HasColumnType("int(11)")
                            .HasColumnName("role_id");
                        j.IndexerProperty<int>("PermissionId")
                            .HasColumnType("int(11)")
                            .HasColumnName("permission_id");
                    });
        });

        modelBuilder.Entity<ScoringMethod>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("scoring_methods")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Name)
                .HasMaxLength(20)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Setting>(entity =>
        {
            entity.HasKey(e => e.Name).HasName("PRIMARY");

            entity
                .ToTable("settings")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasDefaultValueSql("''")
                .HasColumnName("name");
            entity.Property(e => e.Value)
                .HasColumnType("mediumtext")
                .HasColumnName("value");
        });

        modelBuilder.Entity<Source>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("source")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Status>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("status")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("tags")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.Tag1, "tag_unique").IsUnique();

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.Tag1)
                .HasMaxLength(500)
                .HasColumnName("tag");
        });

        modelBuilder.Entity<TagsTaggee>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("tags_taggees")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => new { e.TagId, e.TaggeeId, e.Type }, "tag_taggee_unique").IsUnique();

            entity.HasIndex(e => new { e.TaggeeId, e.Type }, "taggee_type");

            entity.Property(e => e.TagId)
                .HasColumnType("int(11)")
                .HasColumnName("tag_id");
            entity.Property(e => e.TaggeeId)
                .HasColumnType("int(11)")
                .HasColumnName("taggee_id");
            entity.Property(e => e.Type)
                .HasMaxLength(40)
                .HasColumnName("type");
        });

        modelBuilder.Entity<Team>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("team")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Technology>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("technology")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .HasColumnName("name");
        });

        modelBuilder.Entity<TestResult>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("test_results")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.Name, "name_unique").IsUnique();

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.BackgroundClass)
                .HasMaxLength(100)
                .HasColumnName("background_class");
            entity.Property(e => e.Name)
                .HasMaxLength(20)
                .HasColumnName("name");
        });

        modelBuilder.Entity<TestStatus>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("test_status")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
        });

        modelBuilder.Entity<ThreatCatalog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("threat_catalog")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.Description)
                .HasColumnType("text")
                .HasColumnName("description");
            entity.Property(e => e.Grouping)
                .HasColumnType("int(11)")
                .HasColumnName("grouping");
            entity.Property(e => e.Name)
                .HasMaxLength(1000)
                .HasColumnName("name");
            entity.Property(e => e.Number)
                .HasMaxLength(20)
                .HasColumnName("number");
            entity.Property(e => e.Order)
                .HasColumnType("int(11)")
                .HasColumnName("order");
        });

        modelBuilder.Entity<ThreatGrouping>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("threat_grouping")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Default).HasColumnName("default");
            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .HasColumnName("name");
            entity.Property(e => e.Order)
                .HasColumnType("int(11)")
                .HasColumnName("order");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Value).HasName("PRIMARY");

            entity
                .ToTable("user")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.RoleId, "fk_role_user");

            entity.Property(e => e.Value)
                .HasColumnType("int(11)")
                .HasColumnName("value");
            entity.Property(e => e.Admin).HasColumnName("admin");
            entity.Property(e => e.ChangePassword)
                .HasColumnType("tinyint(4)")
                .HasColumnName("change_password");
            entity.Property(e => e.Email)
                .HasColumnType("blob")
                .HasColumnName("email");
            entity.Property(e => e.Enabled)
                .IsRequired()
                .HasDefaultValueSql("'1'")
                .HasColumnName("enabled");
            entity.Property(e => e.Lang)
                .HasMaxLength(5)
                .HasColumnName("lang");
            entity.Property(e => e.LastLogin)
                .HasColumnType("datetime")
                .HasColumnName("last_login");
            entity.Property(e => e.LastPasswordChangeDate)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("timestamp")
                .HasColumnName("last_password_change_date");
            entity.Property(e => e.Lockout)
                .HasColumnType("tinyint(4)")
                .HasColumnName("lockout");
            entity.Property(e => e.Manager)
                .HasColumnType("int(11)")
                .HasColumnName("manager");
            entity.Property(e => e.MultiFactor)
                .HasDefaultValueSql("'1'")
                .HasColumnType("int(11)")
                .HasColumnName("multi_factor");
            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .HasColumnName("name");
            entity.Property(e => e.Password)
                .HasMaxLength(60)
                .IsFixedLength()
                .HasColumnName("password");
            entity.Property(e => e.RoleId)
                .HasColumnType("int(11)")
                .HasColumnName("role_id");
            entity.Property(e => e.Salt)
                .HasMaxLength(20)
                .HasColumnName("salt");
            entity.Property(e => e.Type)
                .HasMaxLength(20)
                .HasDefaultValueSql("'local'")
                .HasColumnName("type");
            entity.Property(e => e.Username)
                .HasColumnType("blob")
                .HasColumnName("username");

            entity.HasOne(d => d.Role).WithMany(p => p.Users)
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_role_user");

            entity.HasMany(d => d.Teams).WithMany(p => p.Users)
                .UsingEntity<Dictionary<string, object>>(
                    "UserToTeam",
                    r => r.HasOne<Team>().WithMany()
                        .HasForeignKey("TeamId")
                        .HasConstraintName("fk_ut_team"),
                    l => l.HasOne<User>().WithMany()
                        .HasForeignKey("UserId")
                        .HasConstraintName("fk_ut_user"),
                    j =>
                    {
                        j.HasKey("UserId", "TeamId")
                            .HasName("PRIMARY")
                            .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });
                        j
                            .ToTable("user_to_team")
                            .HasCharSet("utf8mb3")
                            .UseCollation("utf8mb3_general_ci");
                        j.HasIndex(new[] { "TeamId", "UserId" }, "team_id");
                        j.IndexerProperty<int>("UserId")
                            .HasColumnType("int(11)")
                            .HasColumnName("user_id");
                        j.IndexerProperty<int>("TeamId")
                            .HasColumnType("int(11)")
                            .HasColumnName("team_id");
                    });
            
            entity.HasMany( u => u.IncidentResponsePlans)
                .WithOne(irp => irp.CreatedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_irp_user");
            
            entity.HasMany( u => u.IncidentResponsePlansUpdated)
                .WithOne(irp => irp.UpdatedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_irp_user_update");
            
        });

        modelBuilder.Entity<UserPassHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("user_pass_history")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.AddDate)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("timestamp")
                .HasColumnName("add_date");
            entity.Property(e => e.Password)
                .HasMaxLength(60)
                .IsFixedLength()
                .HasColumnName("password");
            entity.Property(e => e.Salt)
                .HasMaxLength(20)
                .HasColumnName("salt");
            entity.Property(e => e.UserId)
                .HasColumnType("int(11)")
                .HasColumnName("user_id");
        });

        modelBuilder.Entity<UserPassReuseHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("user_pass_reuse_history")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.Counts)
                .HasDefaultValueSql("'1'")
                .HasColumnType("int(11)")
                .HasColumnName("counts");
            entity.Property(e => e.Password)
                .HasMaxLength(60)
                .IsFixedLength()
                .HasColumnName("password");
            entity.Property(e => e.UserId)
                .HasColumnType("int(11)")
                .HasColumnName("user_id");
        });

        modelBuilder.Entity<Vulnerability>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("vulnerabilities")
                .UseCollation("utf8mb4_unicode_ci");

            entity.HasIndex(e => e.HostServiceId, "fk_hosts_service");

            entity.HasIndex(e => e.EntityId, "fk_vul_ent");

            entity.HasIndex(e => e.HostId, "fk_vulnerability_host");

            entity.HasIndex(e => e.FixTeamId, "fk_vulnerability_team");

            entity.HasIndex(e => e.AnalystId, "fk_vulnerarbility_user");

            entity.HasIndex(e => e.Status, "idx_status");

            entity.HasIndex(e => e.Technology, "idx_technology");

            entity.HasIndex(e => e.Title, "idx_title").HasAnnotation("MySql:FullTextIndex", true);

            entity.Property(e => e.Id).HasColumnType("int(11)");
            entity.Property(e => e.AnalystId).HasColumnType("int(11)");
            entity.Property(e => e.Comments).HasColumnType("text");
            entity.Property(e => e.Cves).HasColumnType("text");
            entity.Property(e => e.Cvss3BaseScore).HasDefaultValueSql("'0'");
            entity.Property(e => e.Cvss3ImpactScore).HasDefaultValueSql("'0'");
            entity.Property(e => e.Cvss3TemporalScore).HasDefaultValueSql("'0'");
            entity.Property(e => e.Cvss3TemporalVector).HasMaxLength(255);
            entity.Property(e => e.Cvss3Vector).HasMaxLength(255);
            entity.Property(e => e.CvssBaseScore).HasDefaultValueSql("'0'");
            entity.Property(e => e.CvssScoreSource).HasMaxLength(255);
            entity.Property(e => e.CvssTemporalScore).HasDefaultValueSql("'0'");
            entity.Property(e => e.CvssTemporalVector).HasMaxLength(255);
            entity.Property(e => e.CvssVector).HasMaxLength(255);
            entity.Property(e => e.Description).HasColumnType("text");
            entity.Property(e => e.DetectionCount)
                .HasDefaultValueSql("'1'")
                .HasColumnType("int(11)");
            entity.Property(e => e.EntityId).HasColumnType("int(11)");
            entity.Property(e => e.ExploitCodeMaturity).HasMaxLength(255);
            entity.Property(e => e.ExploitabilityEasy).HasMaxLength(255);
            entity.Property(e => e.FirstDetection)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime");
            entity.Property(e => e.FixTeamId).HasColumnType("int(11)");
            entity.Property(e => e.HostId).HasColumnType("int(11)");
            entity.Property(e => e.HostServiceId).HasColumnType("int(11)");
            entity.Property(e => e.Iava).HasColumnType("text");
            entity.Property(e => e.ImportHash).HasMaxLength(255);
            entity.Property(e => e.ImportSource).HasMaxLength(255);
            entity.Property(e => e.LastDetection)
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime");
            entity.Property(e => e.Msft).HasColumnType("text");
            entity.Property(e => e.Mskb).HasColumnType("text");
            entity.Property(e => e.PatchPublicationDate).HasColumnType("datetime");
            entity.Property(e => e.Severity).HasMaxLength(255);
            entity.Property(e => e.Solution).HasColumnType("text");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'1'")
                .HasColumnType("smallint(5) unsigned");
            entity.Property(e => e.ThreatIntensity).HasMaxLength(255);
            entity.Property(e => e.ThreatRecency).HasMaxLength(255);
            entity.Property(e => e.ThreatSources).HasMaxLength(255);
            entity.Property(e => e.VulnerabilityPublicationDate).HasColumnType("datetime");
            entity.Property(e => e.Xref).HasColumnType("text");

            entity.HasOne(d => d.Analyst).WithMany(p => p.Vulnerabilities)
                .HasForeignKey(d => d.AnalystId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_vulnerarbility_user");

            entity.HasOne(d => d.Entity).WithMany(p => p.Vulnerabilities)
                .HasForeignKey(d => d.EntityId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_vul_ent");

            entity.HasOne(d => d.FixTeam).WithMany(p => p.Vulnerabilities)
                .HasForeignKey(d => d.FixTeamId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_vulnerability_team");

            entity.HasOne(d => d.Host).WithMany(p => p.Vulnerabilities)
                .HasForeignKey(d => d.HostId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_vulnerability_host");

            entity.HasOne(d => d.HostService).WithMany(p => p.Vulnerabilities)
                .HasForeignKey(d => d.HostServiceId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_hosts_service");

            entity.HasMany(d => d.Actions).WithMany(p => p.Vulnerabilities)
                .UsingEntity<Dictionary<string, object>>(
                    "VulnerabilitiesToAction",
                    r => r.HasOne<NrAction>().WithMany()
                        .HasForeignKey("ActionId")
                        .HasConstraintName("fk_vul_act2"),
                    l => l.HasOne<Vulnerability>().WithMany()
                        .HasForeignKey("VulnerabilityId")
                        .HasConstraintName("fk_vul_act_1"),
                    j =>
                    {
                        j.HasKey("VulnerabilityId", "ActionId")
                            .HasName("PRIMARY")
                            .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });
                        j.ToTable("vulnerabilities_to_actions");
                        j.HasIndex(new[] { "ActionId" }, "fk_vul_act2");
                        j.IndexerProperty<int>("VulnerabilityId")
                            .HasColumnType("int(11)")
                            .HasColumnName("vulnerabilityId");
                        j.IndexerProperty<int>("ActionId")
                            .HasColumnType("int(11)")
                            .HasColumnName("actionId");
                    });
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
