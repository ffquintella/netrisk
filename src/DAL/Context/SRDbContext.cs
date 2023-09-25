using System;
using System.Collections.Generic;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

public partial class SRDbContext : DbContext
{
    public SRDbContext(DbContextOptions<SRDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<ApiKey> ApiKeys { get; set; }

    public virtual DbSet<Assessment> Assessments { get; set; }

    public virtual DbSet<AssessmentAnswer> AssessmentAnswers { get; set; }

    public virtual DbSet<AssessmentAnswersToAsset> AssessmentAnswersToAssets { get; set; }

    public virtual DbSet<AssessmentAnswersToAssetGroup> AssessmentAnswersToAssetGroups { get; set; }

    public virtual DbSet<AssessmentQuestion> AssessmentQuestions { get; set; }

    public virtual DbSet<AssessmentScoring> AssessmentScorings { get; set; }

    public virtual DbSet<AssessmentScoringContributingImpact> AssessmentScoringContributingImpacts { get; set; }

    public virtual DbSet<AuditLog> AuditLogs { get; set; }

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

    public virtual DbSet<Entities.File> Files { get; set; }

    public virtual DbSet<FileType> FileTypes { get; set; }

    public virtual DbSet<FileTypeExtension> FileTypeExtensions { get; set; }

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

    public virtual DbSet<Impact> Impacts { get; set; }

    public virtual DbSet<Likelihood> Likelihoods { get; set; }

    public virtual DbSet<Link> Links { get; set; }

    public virtual DbSet<Location> Locations { get; set; }

    public virtual DbSet<MgmtReview> MgmtReviews { get; set; }

    public virtual DbSet<Mitigation> Mitigations { get; set; }

    public virtual DbSet<MitigationAcceptUser> MitigationAcceptUsers { get; set; }

    public virtual DbSet<MitigationCost> MitigationCosts { get; set; }

    public virtual DbSet<MitigationEffort> MitigationEfforts { get; set; }

    public virtual DbSet<MitigationToControl> MitigationToControls { get; set; }

    public virtual DbSet<MitigationToTeam> MitigationToTeams { get; set; }

    public virtual DbSet<NextStep> NextSteps { get; set; }

    public virtual DbSet<PendingRisk> PendingRisks { get; set; }

    public virtual DbSet<Permission> Permissions { get; set; }

    public virtual DbSet<PermissionGroup> PermissionGroups { get; set; }

    public virtual DbSet<PermissionToPermissionGroup> PermissionToPermissionGroups { get; set; }

    public virtual DbSet<PlanningStrategy> PlanningStrategies { get; set; }

    public virtual DbSet<QuestionnairePendingRisk> QuestionnairePendingRisks { get; set; }

    public virtual DbSet<Regulation> Regulations { get; set; }

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

    public virtual DbSet<RisksToAsset> RisksToAssets { get; set; }

    public virtual DbSet<RisksToAssetGroup> RisksToAssetGroups { get; set; }

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

    public virtual DbSet<UserToTeam> UserToTeams { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .UseCollation("utf8mb4_unicode_ci")
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
        });

        modelBuilder.Entity<AssessmentAnswersToAsset>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("assessment_answers_to_assets")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => new { e.AssessmentAnswerId, e.AssetId }, "assessment_answer_asset_unique").IsUnique();

            entity.Property(e => e.AssessmentAnswerId)
                .HasColumnType("int(11)")
                .HasColumnName("assessment_answer_id");
            entity.Property(e => e.AssetId)
                .HasColumnType("int(11)")
                .HasColumnName("asset_id");
        });

        modelBuilder.Entity<AssessmentAnswersToAssetGroup>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("assessment_answers_to_asset_groups")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => new { e.AssessmentAnswerId, e.AssetGroupId }, "assessment_answer_asset_group_unique").IsUnique();

            entity.Property(e => e.AssessmentAnswerId)
                .HasColumnType("int(11)")
                .HasColumnName("assessment_answer_id");
            entity.Property(e => e.AssetGroupId)
                .HasColumnType("int(11)")
                .HasColumnName("asset_group_id");
        });

        modelBuilder.Entity<AssessmentQuestion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("assessment_questions")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

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
        });

        modelBuilder.Entity<AssessmentScoring>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("assessment_scoring")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.Id, "id").IsUnique();

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.CalculatedRisk).HasColumnName("calculated_risk");
            entity.Property(e => e.ClassicImpact)
                .HasDefaultValueSql("'5'")
                .HasColumnName("CLASSIC_impact");
            entity.Property(e => e.ClassicLikelihood)
                .HasDefaultValueSql("'5'")
                .HasColumnName("CLASSIC_likelihood");
            entity.Property(e => e.ContributingLikelihood)
                .HasDefaultValueSql("'0'")
                .HasColumnType("int(11)")
                .HasColumnName("Contributing_Likelihood");
            entity.Property(e => e.Custom).HasDefaultValueSql("'10'");
            entity.Property(e => e.CvssAccessComplexity)
                .HasMaxLength(3)
                .HasDefaultValueSql("'L'")
                .HasColumnName("CVSS_AccessComplexity");
            entity.Property(e => e.CvssAccessVector)
                .HasMaxLength(3)
                .HasDefaultValueSql("'N'")
                .HasColumnName("CVSS_AccessVector");
            entity.Property(e => e.CvssAuthentication)
                .HasMaxLength(3)
                .HasDefaultValueSql("'N'")
                .HasColumnName("CVSS_Authentication");
            entity.Property(e => e.CvssAvailImpact)
                .HasMaxLength(3)
                .HasDefaultValueSql("'C'")
                .HasColumnName("CVSS_AvailImpact");
            entity.Property(e => e.CvssAvailabilityRequirement)
                .HasMaxLength(3)
                .HasDefaultValueSql("'ND'")
                .HasColumnName("CVSS_AvailabilityRequirement");
            entity.Property(e => e.CvssCollateralDamagePotential)
                .HasMaxLength(3)
                .HasDefaultValueSql("'ND'")
                .HasColumnName("CVSS_CollateralDamagePotential");
            entity.Property(e => e.CvssConfImpact)
                .HasMaxLength(3)
                .HasDefaultValueSql("'C'")
                .HasColumnName("CVSS_ConfImpact");
            entity.Property(e => e.CvssConfidentialityRequirement)
                .HasMaxLength(3)
                .HasDefaultValueSql("'ND'")
                .HasColumnName("CVSS_ConfidentialityRequirement");
            entity.Property(e => e.CvssExploitability)
                .HasMaxLength(3)
                .HasDefaultValueSql("'ND'")
                .HasColumnName("CVSS_Exploitability");
            entity.Property(e => e.CvssIntegImpact)
                .HasMaxLength(3)
                .HasDefaultValueSql("'C'")
                .HasColumnName("CVSS_IntegImpact");
            entity.Property(e => e.CvssIntegrityRequirement)
                .HasMaxLength(3)
                .HasDefaultValueSql("'ND'")
                .HasColumnName("CVSS_IntegrityRequirement");
            entity.Property(e => e.CvssRemediationLevel)
                .HasMaxLength(3)
                .HasDefaultValueSql("'ND'")
                .HasColumnName("CVSS_RemediationLevel");
            entity.Property(e => e.CvssReportConfidence)
                .HasMaxLength(3)
                .HasDefaultValueSql("'ND'")
                .HasColumnName("CVSS_ReportConfidence");
            entity.Property(e => e.CvssTargetDistribution)
                .HasMaxLength(3)
                .HasDefaultValueSql("'ND'")
                .HasColumnName("CVSS_TargetDistribution");
            entity.Property(e => e.DreadAffectedUsers)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("DREAD_AffectedUsers");
            entity.Property(e => e.DreadDamagePotential)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("DREAD_DamagePotential");
            entity.Property(e => e.DreadDiscoverability)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("DREAD_Discoverability");
            entity.Property(e => e.DreadExploitability)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("DREAD_Exploitability");
            entity.Property(e => e.DreadReproducibility)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("DREAD_Reproducibility");
            entity.Property(e => e.OwaspAwareness)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_Awareness");
            entity.Property(e => e.OwaspEaseOfDiscovery)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_EaseOfDiscovery");
            entity.Property(e => e.OwaspEaseOfExploit)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_EaseOfExploit");
            entity.Property(e => e.OwaspFinancialDamage)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_FinancialDamage");
            entity.Property(e => e.OwaspIntrusionDetection)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_IntrusionDetection");
            entity.Property(e => e.OwaspLossOfAccountability)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_LossOfAccountability");
            entity.Property(e => e.OwaspLossOfAvailability)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_LossOfAvailability");
            entity.Property(e => e.OwaspLossOfConfidentiality)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_LossOfConfidentiality");
            entity.Property(e => e.OwaspLossOfIntegrity)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_LossOfIntegrity");
            entity.Property(e => e.OwaspMotive)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_Motive");
            entity.Property(e => e.OwaspNonCompliance)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_NonCompliance");
            entity.Property(e => e.OwaspOpportunity)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_Opportunity");
            entity.Property(e => e.OwaspPrivacyViolation)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_PrivacyViolation");
            entity.Property(e => e.OwaspReputationDamage)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_ReputationDamage");
            entity.Property(e => e.OwaspSize)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_Size");
            entity.Property(e => e.OwaspSkillLevel)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_SkillLevel");
            entity.Property(e => e.ScoringMethod)
                .HasColumnType("int(11)")
                .HasColumnName("scoring_method");
        });

        modelBuilder.Entity<AssessmentScoringContributingImpact>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("assessment_scoring_contributing_impacts")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.AssessmentScoringId)
                .HasColumnType("int(11)")
                .HasColumnName("assessment_scoring_id");
            entity.Property(e => e.ContributingRiskId)
                .HasColumnType("int(11)")
                .HasColumnName("contributing_risk_id");
            entity.Property(e => e.Impact)
                .HasColumnType("int(11)")
                .HasColumnName("impact");
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("audit_log")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.LogType)
                .HasMaxLength(100)
                .HasColumnName("log_type");
            entity.Property(e => e.Message)
                .HasColumnType("text")
                .HasColumnName("message");
            entity.Property(e => e.RiskId)
                .HasColumnType("int(11)")
                .HasColumnName("risk_id");
            entity.Property(e => e.Timestamp)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("timestamp")
                .HasColumnName("timestamp");
            entity.Property(e => e.UserId)
                .HasColumnType("int(11)")
                .HasColumnName("user_id");
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

            entity
                .ToTable("comments")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.Comment1)
                .HasColumnType("text")
                .HasColumnName("comment");
            entity.Property(e => e.Date)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("timestamp")
                .HasColumnName("date");
            entity.Property(e => e.RiskId)
                .HasColumnType("int(11)")
                .HasColumnName("risk_id");
            entity.Property(e => e.User)
                .HasColumnType("int(11)")
                .HasColumnName("user");
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

            entity
                .ToTable("entities_properties")
                .UseCollation("utf8mb4_general_ci");

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

            entity
                .ToTable("entities")
                .UseCollation("utf8mb4_general_ci");

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

        modelBuilder.Entity<Entities.File>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("files")
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

            entity
                .ToTable("links")
                .UseCollation("utf8mb4_general_ci");

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

            entity
                .ToTable("mitigation_cost")
                .UseCollation("utf8mb4_general_ci");

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
                .HasDefaultValueSql("'0'")
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

            entity.HasOne(d => d.Mitigation).WithMany(p => p.Risks)
                .HasForeignKey(d => d.MitigationId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_risk_mitigation");

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
                        j
                            .ToTable("risk_to_entity")
                            .UseCollation("utf8mb4_general_ci");
                        j.HasIndex(new[] { "EntityId" }, "fk_entity_id");
                        j.IndexerProperty<int>("RiskId")
                            .HasColumnType("int(11)")
                            .HasColumnName("risk_id");
                        j.IndexerProperty<int>("EntityId")
                            .HasColumnType("int(11)")
                            .HasColumnName("entity_id");
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
            entity.Property(e => e.ContributingLikelihood)
                .HasDefaultValueSql("'0'")
                .HasColumnType("int(11)")
                .HasColumnName("Contributing_Likelihood");
            entity.Property(e => e.Custom).HasDefaultValueSql("'10'");
            entity.Property(e => e.CvssAccessComplexity)
                .HasMaxLength(3)
                .HasDefaultValueSql("'L'")
                .HasColumnName("CVSS_AccessComplexity");
            entity.Property(e => e.CvssAccessVector)
                .HasMaxLength(3)
                .HasDefaultValueSql("'N'")
                .HasColumnName("CVSS_AccessVector");
            entity.Property(e => e.CvssAuthentication)
                .HasMaxLength(3)
                .HasDefaultValueSql("'N'")
                .HasColumnName("CVSS_Authentication");
            entity.Property(e => e.CvssAvailImpact)
                .HasMaxLength(3)
                .HasDefaultValueSql("'C'")
                .HasColumnName("CVSS_AvailImpact");
            entity.Property(e => e.CvssAvailabilityRequirement)
                .HasMaxLength(3)
                .HasDefaultValueSql("'ND'")
                .HasColumnName("CVSS_AvailabilityRequirement");
            entity.Property(e => e.CvssCollateralDamagePotential)
                .HasMaxLength(3)
                .HasDefaultValueSql("'ND'")
                .HasColumnName("CVSS_CollateralDamagePotential");
            entity.Property(e => e.CvssConfImpact)
                .HasMaxLength(3)
                .HasDefaultValueSql("'C'")
                .HasColumnName("CVSS_ConfImpact");
            entity.Property(e => e.CvssConfidentialityRequirement)
                .HasMaxLength(3)
                .HasDefaultValueSql("'ND'")
                .HasColumnName("CVSS_ConfidentialityRequirement");
            entity.Property(e => e.CvssExploitability)
                .HasMaxLength(3)
                .HasDefaultValueSql("'ND'")
                .HasColumnName("CVSS_Exploitability");
            entity.Property(e => e.CvssIntegImpact)
                .HasMaxLength(3)
                .HasDefaultValueSql("'C'")
                .HasColumnName("CVSS_IntegImpact");
            entity.Property(e => e.CvssIntegrityRequirement)
                .HasMaxLength(3)
                .HasDefaultValueSql("'ND'")
                .HasColumnName("CVSS_IntegrityRequirement");
            entity.Property(e => e.CvssRemediationLevel)
                .HasMaxLength(3)
                .HasDefaultValueSql("'ND'")
                .HasColumnName("CVSS_RemediationLevel");
            entity.Property(e => e.CvssReportConfidence)
                .HasMaxLength(3)
                .HasDefaultValueSql("'ND'")
                .HasColumnName("CVSS_ReportConfidence");
            entity.Property(e => e.CvssTargetDistribution)
                .HasMaxLength(3)
                .HasDefaultValueSql("'ND'")
                .HasColumnName("CVSS_TargetDistribution");
            entity.Property(e => e.DreadAffectedUsers)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("DREAD_AffectedUsers");
            entity.Property(e => e.DreadDamagePotential)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("DREAD_DamagePotential");
            entity.Property(e => e.DreadDiscoverability)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("DREAD_Discoverability");
            entity.Property(e => e.DreadExploitability)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("DREAD_Exploitability");
            entity.Property(e => e.DreadReproducibility)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("DREAD_Reproducibility");
            entity.Property(e => e.OwaspAwareness)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_Awareness");
            entity.Property(e => e.OwaspEaseOfDiscovery)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_EaseOfDiscovery");
            entity.Property(e => e.OwaspEaseOfExploit)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_EaseOfExploit");
            entity.Property(e => e.OwaspFinancialDamage)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_FinancialDamage");
            entity.Property(e => e.OwaspIntrusionDetection)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_IntrusionDetection");
            entity.Property(e => e.OwaspLossOfAccountability)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_LossOfAccountability");
            entity.Property(e => e.OwaspLossOfAvailability)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_LossOfAvailability");
            entity.Property(e => e.OwaspLossOfConfidentiality)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_LossOfConfidentiality");
            entity.Property(e => e.OwaspLossOfIntegrity)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_LossOfIntegrity");
            entity.Property(e => e.OwaspMotive)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_Motive");
            entity.Property(e => e.OwaspNonCompliance)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_NonCompliance");
            entity.Property(e => e.OwaspOpportunity)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_Opportunity");
            entity.Property(e => e.OwaspPrivacyViolation)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_PrivacyViolation");
            entity.Property(e => e.OwaspReputationDamage)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_ReputationDamage");
            entity.Property(e => e.OwaspSize)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_Size");
            entity.Property(e => e.OwaspSkillLevel)
                .HasDefaultValueSql("'10'")
                .HasColumnType("int(11)")
                .HasColumnName("OWASP_SkillLevel");
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

        modelBuilder.Entity<RisksToAsset>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("risks_to_assets")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => new { e.AssetId, e.RiskId }, "asset_id").IsUnique();

            entity.Property(e => e.AssetId)
                .HasColumnType("int(11)")
                .HasColumnName("asset_id");
            entity.Property(e => e.RiskId)
                .HasColumnType("int(11)")
                .HasColumnName("risk_id");
        });

        modelBuilder.Entity<RisksToAssetGroup>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("risks_to_asset_groups")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => new { e.AssetGroupId, e.RiskId }, "asset_group_id").IsUnique();

            entity.Property(e => e.AssetGroupId)
                .HasColumnType("int(11)")
                .HasColumnName("asset_group_id");
            entity.Property(e => e.RiskId)
                .HasColumnType("int(11)")
                .HasColumnName("risk_id");
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

        modelBuilder.Entity<UserToTeam>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.TeamId })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

            entity
                .ToTable("user_to_team")
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => new { e.TeamId, e.UserId }, "team_id");

            entity.Property(e => e.UserId)
                .HasColumnType("int(11)")
                .HasColumnName("user_id");
            entity.Property(e => e.TeamId)
                .HasColumnType("int(11)")
                .HasColumnName("team_id");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
