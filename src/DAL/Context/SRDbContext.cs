using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using DAL.Entities;

namespace DAL.Context
{
    public partial class SRDbContext : DbContext
    {
        public SRDbContext()
        {
        }

        public SRDbContext(DbContextOptions<SRDbContext> options)
            : base(options)
        {
        }

        public virtual DbSet<AddonsApiKey> AddonsApiKeys { get; set; } = null!;
        public virtual DbSet<AddonsClientRegistration> AddonsClientRegistrations { get; set; } = null!;
        public virtual DbSet<Assessment> Assessments { get; set; } = null!;
        public virtual DbSet<AssessmentAnswer> AssessmentAnswers { get; set; } = null!;
        public virtual DbSet<AssessmentAnswersToAsset> AssessmentAnswersToAssets { get; set; } = null!;
        public virtual DbSet<AssessmentAnswersToAssetGroup> AssessmentAnswersToAssetGroups { get; set; } = null!;
        public virtual DbSet<AssessmentQuestion> AssessmentQuestions { get; set; } = null!;
        public virtual DbSet<AssessmentScoring> AssessmentScorings { get; set; } = null!;
        public virtual DbSet<AssessmentScoringContributingImpact> AssessmentScoringContributingImpacts { get; set; } = null!;
        public virtual DbSet<Asset> Assets { get; set; } = null!;
        public virtual DbSet<AssetGroup> AssetGroups { get; set; } = null!;
        public virtual DbSet<AssetValue> AssetValues { get; set; } = null!;
        public virtual DbSet<AssetsAssetGroup> AssetsAssetGroups { get; set; } = null!;
        public virtual DbSet<AuditLog> AuditLogs { get; set; } = null!;
        public virtual DbSet<Backup> Backups { get; set; } = null!;
        public virtual DbSet<Category> Categories { get; set; } = null!;
        public virtual DbSet<CloseReason> CloseReasons { get; set; } = null!;
        public virtual DbSet<Closure> Closures { get; set; } = null!;
        public virtual DbSet<Comment> Comments { get; set; } = null!;
        public virtual DbSet<ComplianceFile> ComplianceFiles { get; set; } = null!;
        public virtual DbSet<ContributingRisk> ContributingRisks { get; set; } = null!;
        public virtual DbSet<ContributingRisksImpact> ContributingRisksImpacts { get; set; } = null!;
        public virtual DbSet<ContributingRisksLikelihood> ContributingRisksLikelihoods { get; set; } = null!;
        public virtual DbSet<ControlClass> ControlClasses { get; set; } = null!;
        public virtual DbSet<ControlMaturity> ControlMaturities { get; set; } = null!;
        public virtual DbSet<ControlPhase> ControlPhases { get; set; } = null!;
        public virtual DbSet<ControlPriority> ControlPriorities { get; set; } = null!;
        public virtual DbSet<ControlType> ControlTypes { get; set; } = null!;
        public virtual DbSet<CustomRiskModelValue> CustomRiskModelValues { get; set; } = null!;
        public virtual DbSet<CvssScoring> CvssScorings { get; set; } = null!;
        public virtual DbSet<DataClassification> DataClassifications { get; set; } = null!;
        public virtual DbSet<DateFormat> DateFormats { get; set; } = null!;
        public virtual DbSet<Document> Documents { get; set; } = null!;
        public virtual DbSet<DocumentException> DocumentExceptions { get; set; } = null!;
        public virtual DbSet<DocumentExceptionsStatus> DocumentExceptionsStatuses { get; set; } = null!;
        public virtual DbSet<DocumentStatus> DocumentStatuses { get; set; } = null!;
        public virtual DbSet<DynamicSavedSelection> DynamicSavedSelections { get; set; } = null!;
        public virtual DbSet<EntitiesProperty> EntitiesProperties { get; set; } = null!;
        public virtual DbSet<Entity> Entities { get; set; } = null!;
        public virtual DbSet<FailedLoginAttempt> FailedLoginAttempts { get; set; } = null!;
        public virtual DbSet<Family> Families { get; set; } = null!;
        public virtual DbSet<Field> Fields { get; set; } = null!;
        public virtual DbSet<DAL.Entities.File> Files { get; set; } = null!;
        public virtual DbSet<FileType> FileTypes { get; set; } = null!;
        public virtual DbSet<FileTypeExtension> FileTypeExtensions { get; set; } = null!;
        public virtual DbSet<Framework> Frameworks { get; set; } = null!;
        public virtual DbSet<FrameworkControl> FrameworkControls { get; set; } = null!;
        public virtual DbSet<FrameworkControlMapping> FrameworkControlMappings { get; set; } = null!;
        public virtual DbSet<FrameworkControlTest> FrameworkControlTests { get; set; } = null!;
        public virtual DbSet<FrameworkControlTestAudit> FrameworkControlTestAudits { get; set; } = null!;
        public virtual DbSet<FrameworkControlTestComment> FrameworkControlTestComments { get; set; } = null!;
        public virtual DbSet<FrameworkControlTestResult> FrameworkControlTestResults { get; set; } = null!;
        public virtual DbSet<FrameworkControlTestResultsToRisk> FrameworkControlTestResultsToRisks { get; set; } = null!;
        public virtual DbSet<FrameworkControlToFramework> FrameworkControlToFrameworks { get; set; } = null!;
        public virtual DbSet<FrameworkControlTypeMapping> FrameworkControlTypeMappings { get; set; } = null!;
        public virtual DbSet<GraphicalSavedSelection> GraphicalSavedSelections { get; set; } = null!;
        public virtual DbSet<Impact> Impacts { get; set; } = null!;
        public virtual DbSet<ItemsToTeam> ItemsToTeams { get; set; } = null!;
        public virtual DbSet<Language> Languages { get; set; } = null!;
        public virtual DbSet<Likelihood> Likelihoods { get; set; } = null!;
        public virtual DbSet<Link> Links { get; set; } = null!;
        public virtual DbSet<Location> Locations { get; set; } = null!;
        public virtual DbSet<MgmtReview> MgmtReviews { get; set; } = null!;
        public virtual DbSet<Mitigation> Mitigations { get; set; } = null!;
        public virtual DbSet<MitigationAcceptUser> MitigationAcceptUsers { get; set; } = null!;
        public virtual DbSet<MitigationCost> MitigationCosts { get; set; } = null!;
        public virtual DbSet<MitigationEffort> MitigationEfforts { get; set; } = null!;
        public virtual DbSet<MitigationToControl> MitigationToControls { get; set; } = null!;
        public virtual DbSet<MitigationToTeam> MitigationToTeams { get; set; } = null!;
        public virtual DbSet<NextStep> NextSteps { get; set; } = null!;
        public virtual DbSet<PasswordReset> PasswordResets { get; set; } = null!;
        public virtual DbSet<PendingRisk> PendingRisks { get; set; } = null!;
        public virtual DbSet<Permission> Permissions { get; set; } = null!;
        public virtual DbSet<PermissionGroup> PermissionGroups { get; set; } = null!;
        public virtual DbSet<PermissionToPermissionGroup> PermissionToPermissionGroups { get; set; } = null!;
        public virtual DbSet<PermissionToUser> PermissionToUsers { get; set; } = null!;
        public virtual DbSet<PlanningStrategy> PlanningStrategies { get; set; } = null!;
        public virtual DbSet<Project> Projects { get; set; } = null!;
        public virtual DbSet<Regulation> Regulations { get; set; } = null!;
        public virtual DbSet<ResidualRiskScoringHistory> ResidualRiskScoringHistories { get; set; } = null!;
        public virtual DbSet<Review> Reviews { get; set; } = null!;
        public virtual DbSet<ReviewLevel> ReviewLevels { get; set; } = null!;
        public virtual DbSet<Risk> Risks { get; set; } = null!;
        public virtual DbSet<RiskCatalog> RiskCatalogs { get; set; } = null!;
        public virtual DbSet<RiskFunction> RiskFunctions { get; set; } = null!;
        public virtual DbSet<RiskGrouping> RiskGroupings { get; set; } = null!;
        public virtual DbSet<RiskLevel> RiskLevels { get; set; } = null!;
        public virtual DbSet<RiskModel> RiskModels { get; set; } = null!;
        public virtual DbSet<RiskScoring> RiskScorings { get; set; } = null!;
        public virtual DbSet<RiskScoringContributingImpact> RiskScoringContributingImpacts { get; set; } = null!;
        public virtual DbSet<RiskScoringHistory> RiskScoringHistories { get; set; } = null!;
        public virtual DbSet<RiskToAdditionalStakeholder> RiskToAdditionalStakeholders { get; set; } = null!;
        public virtual DbSet<RiskToLocation> RiskToLocations { get; set; } = null!;
        public virtual DbSet<RiskToTeam> RiskToTeams { get; set; } = null!;
        public virtual DbSet<RiskToTechnology> RiskToTechnologies { get; set; } = null!;
        public virtual DbSet<RisksToAsset> RisksToAssets { get; set; } = null!;
        public virtual DbSet<RisksToAssetGroup> RisksToAssetGroups { get; set; } = null!;
        public virtual DbSet<Role> Roles { get; set; } = null!;
        public virtual DbSet<RoleResponsibility> RoleResponsibilities { get; set; } = null!;
        public virtual DbSet<SavedTableDisplaySetting> SavedTableDisplaySettings { get; set; } = null!;
        public virtual DbSet<ScoringMethod> ScoringMethods { get; set; } = null!;
        public virtual DbSet<Session> Sessions { get; set; } = null!;
        public virtual DbSet<Setting> Settings { get; set; } = null!;
        public virtual DbSet<Source> Sources { get; set; } = null!;
        public virtual DbSet<Status> Statuses { get; set; } = null!;
        public virtual DbSet<Tag> Tags { get; set; } = null!;
        public virtual DbSet<TagsTaggee> TagsTaggees { get; set; } = null!;
        public virtual DbSet<Team> Teams { get; set; } = null!;
        public virtual DbSet<Technology> Technologies { get; set; } = null!;
        public virtual DbSet<TestResult> TestResults { get; set; } = null!;
        public virtual DbSet<TestStatus> TestStatuses { get; set; } = null!;
        public virtual DbSet<ThreatCatalog> ThreatCatalogs { get; set; } = null!;
        public virtual DbSet<ThreatGrouping> ThreatGroupings { get; set; } = null!;
        public virtual DbSet<User> Users { get; set; } = null!;
        public virtual DbSet<UserMfa> UserMfas { get; set; } = null!;
        public virtual DbSet<UserPassHistory> UserPassHistories { get; set; } = null!;
        public virtual DbSet<UserPassReuseHistory> UserPassReuseHistories { get; set; } = null!;
        public virtual DbSet<UserToTeam> UserToTeams { get; set; } = null!;
        public virtual DbSet<ValidationFile> ValidationFiles { get; set; } = null!;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UseCollation("utf8mb4_general_ci")
                .HasCharSet("utf8mb4");

            modelBuilder.Entity<AddonsApiKey>(entity =>
            {
                entity.ToTable("addons_api_keys");

                entity.Property(e => e.Id)
                    .HasColumnType("int(6) unsigned")
                    .HasColumnName("id");

                entity.Property(e => e.CreationDate)
                    .HasColumnType("timestamp")
                    .ValueGeneratedOnAddOrUpdate()
                    .HasColumnName("creation_date")
                    .HasDefaultValueSql("current_timestamp()");

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

            modelBuilder.Entity<AddonsClientRegistration>(entity =>
            {
                entity.ToTable("addons_client_registration");

                entity.HasIndex(e => e.ExternalId, "ExternalId");

                entity.Property(e => e.Id).HasColumnType("int(11)");

                entity.Property(e => e.Hostname).HasMaxLength(255);

                entity.Property(e => e.LastVerificationDate)
                    .HasColumnType("datetime")
                    .ValueGeneratedOnAddOrUpdate()
                    .HasDefaultValueSql("'0000-00-00 00:00:00'");

                entity.Property(e => e.LoggedAccount).HasMaxLength(255);

                entity.Property(e => e.Name).HasMaxLength(255);

                entity.Property(e => e.RegistrationDate)
                    .HasColumnType("datetime")
                    .ValueGeneratedOnAddOrUpdate()
                    .HasDefaultValueSql("'0000-00-00 00:00:00'");

                entity.Property(e => e.Status)
                    .HasMaxLength(255)
                    .HasDefaultValueSql("'requested'");
            });

            modelBuilder.Entity<Assessment>(entity =>
            {
                entity.ToTable("assessments");

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .HasColumnName("id");

                entity.Property(e => e.Created)
                    .HasColumnType("timestamp")
                    .HasColumnName("created")
                    .HasDefaultValueSql("current_timestamp()");

                entity.Property(e => e.Name)
                    .HasMaxLength(200)
                    .HasColumnName("name");
            });

            modelBuilder.Entity<AssessmentAnswer>(entity =>
            {
                entity.ToTable("assessment_answers");

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
                    .HasColumnType("int(11)")
                    .HasColumnName("order")
                    .HasDefaultValueSql("'999999'");

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
                entity.HasNoKey();

                entity.ToTable("assessment_answers_to_assets");

                entity.HasIndex(e => new { e.AssessmentAnswerId, e.AssetId }, "assessment_answer_asset_unique")
                    .IsUnique();

                entity.Property(e => e.AssessmentAnswerId)
                    .HasColumnType("int(11)")
                    .HasColumnName("assessment_answer_id");

                entity.Property(e => e.AssetId)
                    .HasColumnType("int(11)")
                    .HasColumnName("asset_id");
            });

            modelBuilder.Entity<AssessmentAnswersToAssetGroup>(entity =>
            {
                entity.HasNoKey();

                entity.ToTable("assessment_answers_to_asset_groups");

                entity.HasIndex(e => new { e.AssessmentAnswerId, e.AssetGroupId }, "assessment_answer_asset_group_unique")
                    .IsUnique();

                entity.Property(e => e.AssessmentAnswerId)
                    .HasColumnType("int(11)")
                    .HasColumnName("assessment_answer_id");

                entity.Property(e => e.AssetGroupId)
                    .HasColumnType("int(11)")
                    .HasColumnName("asset_group_id");
            });

            modelBuilder.Entity<AssessmentQuestion>(entity =>
            {
                entity.ToTable("assessment_questions");

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .HasColumnName("id");

                entity.Property(e => e.AssessmentId)
                    .HasColumnType("int(11)")
                    .HasColumnName("assessment_id");

                entity.Property(e => e.Order)
                    .HasColumnType("int(11)")
                    .HasColumnName("order")
                    .HasDefaultValueSql("'999999'");

                entity.Property(e => e.Question)
                    .HasMaxLength(1000)
                    .HasColumnName("question");
            });

            modelBuilder.Entity<AssessmentScoring>(entity =>
            {
                entity.ToTable("assessment_scoring");

                entity.HasIndex(e => e.Id, "id")
                    .IsUnique();

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .HasColumnName("id");

                entity.Property(e => e.CalculatedRisk).HasColumnName("calculated_risk");

                entity.Property(e => e.ClassicImpact)
                    .HasColumnName("CLASSIC_impact")
                    .HasDefaultValueSql("'5'");

                entity.Property(e => e.ClassicLikelihood)
                    .HasColumnName("CLASSIC_likelihood")
                    .HasDefaultValueSql("'5'");

                entity.Property(e => e.ContributingLikelihood)
                    .HasColumnType("int(11)")
                    .HasColumnName("Contributing_Likelihood")
                    .HasDefaultValueSql("'0'");

                entity.Property(e => e.Custom).HasDefaultValueSql("'10'");

                entity.Property(e => e.CvssAccessComplexity)
                    .HasMaxLength(3)
                    .HasColumnName("CVSS_AccessComplexity")
                    .HasDefaultValueSql("'L'");

                entity.Property(e => e.CvssAccessVector)
                    .HasMaxLength(3)
                    .HasColumnName("CVSS_AccessVector")
                    .HasDefaultValueSql("'N'");

                entity.Property(e => e.CvssAuthentication)
                    .HasMaxLength(3)
                    .HasColumnName("CVSS_Authentication")
                    .HasDefaultValueSql("'N'");

                entity.Property(e => e.CvssAvailImpact)
                    .HasMaxLength(3)
                    .HasColumnName("CVSS_AvailImpact")
                    .HasDefaultValueSql("'C'");

                entity.Property(e => e.CvssAvailabilityRequirement)
                    .HasMaxLength(3)
                    .HasColumnName("CVSS_AvailabilityRequirement")
                    .HasDefaultValueSql("'ND'");

                entity.Property(e => e.CvssCollateralDamagePotential)
                    .HasMaxLength(3)
                    .HasColumnName("CVSS_CollateralDamagePotential")
                    .HasDefaultValueSql("'ND'");

                entity.Property(e => e.CvssConfImpact)
                    .HasMaxLength(3)
                    .HasColumnName("CVSS_ConfImpact")
                    .HasDefaultValueSql("'C'");

                entity.Property(e => e.CvssConfidentialityRequirement)
                    .HasMaxLength(3)
                    .HasColumnName("CVSS_ConfidentialityRequirement")
                    .HasDefaultValueSql("'ND'");

                entity.Property(e => e.CvssExploitability)
                    .HasMaxLength(3)
                    .HasColumnName("CVSS_Exploitability")
                    .HasDefaultValueSql("'ND'");

                entity.Property(e => e.CvssIntegImpact)
                    .HasMaxLength(3)
                    .HasColumnName("CVSS_IntegImpact")
                    .HasDefaultValueSql("'C'");

                entity.Property(e => e.CvssIntegrityRequirement)
                    .HasMaxLength(3)
                    .HasColumnName("CVSS_IntegrityRequirement")
                    .HasDefaultValueSql("'ND'");

                entity.Property(e => e.CvssRemediationLevel)
                    .HasMaxLength(3)
                    .HasColumnName("CVSS_RemediationLevel")
                    .HasDefaultValueSql("'ND'");

                entity.Property(e => e.CvssReportConfidence)
                    .HasMaxLength(3)
                    .HasColumnName("CVSS_ReportConfidence")
                    .HasDefaultValueSql("'ND'");

                entity.Property(e => e.CvssTargetDistribution)
                    .HasMaxLength(3)
                    .HasColumnName("CVSS_TargetDistribution")
                    .HasDefaultValueSql("'ND'");

                entity.Property(e => e.DreadAffectedUsers)
                    .HasColumnType("int(11)")
                    .HasColumnName("DREAD_AffectedUsers")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.DreadDamagePotential)
                    .HasColumnType("int(11)")
                    .HasColumnName("DREAD_DamagePotential")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.DreadDiscoverability)
                    .HasColumnType("int(11)")
                    .HasColumnName("DREAD_Discoverability")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.DreadExploitability)
                    .HasColumnType("int(11)")
                    .HasColumnName("DREAD_Exploitability")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.DreadReproducibility)
                    .HasColumnType("int(11)")
                    .HasColumnName("DREAD_Reproducibility")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspAwareness)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_Awareness")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspEaseOfDiscovery)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_EaseOfDiscovery")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspEaseOfExploit)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_EaseOfExploit")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspFinancialDamage)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_FinancialDamage")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspIntrusionDetection)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_IntrusionDetection")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspLossOfAccountability)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_LossOfAccountability")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspLossOfAvailability)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_LossOfAvailability")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspLossOfConfidentiality)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_LossOfConfidentiality")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspLossOfIntegrity)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_LossOfIntegrity")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspMotive)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_Motive")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspNonCompliance)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_NonCompliance")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspOpportunity)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_Opportunity")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspPrivacyViolation)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_PrivacyViolation")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspReputationDamage)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_ReputationDamage")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspSize)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_Size")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspSkillLevel)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_SkillLevel")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.ScoringMethod)
                    .HasColumnType("int(11)")
                    .HasColumnName("scoring_method");
            });

            modelBuilder.Entity<AssessmentScoringContributingImpact>(entity =>
            {
                entity.ToTable("assessment_scoring_contributing_impacts");

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

            modelBuilder.Entity<Asset>(entity =>
            {
                entity.ToTable("assets");

                entity.HasIndex(e => e.Name, "name")
                    .IsUnique();

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .HasColumnName("id");

                entity.Property(e => e.Created)
                    .HasColumnType("timestamp")
                    .HasColumnName("created")
                    .HasDefaultValueSql("current_timestamp()");

                entity.Property(e => e.Details).HasColumnName("details");

                entity.Property(e => e.Ip)
                    .HasMaxLength(15)
                    .HasColumnName("ip");

                entity.Property(e => e.Location)
                    .HasMaxLength(1000)
                    .HasColumnName("location");

                entity.Property(e => e.Name)
                    .HasMaxLength(200)
                    .HasColumnName("name");

                entity.Property(e => e.Teams)
                    .HasMaxLength(1000)
                    .HasColumnName("teams");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value")
                    .HasDefaultValueSql("'5'");

                entity.Property(e => e.Verified)
                    .HasColumnType("tinyint(4)")
                    .HasColumnName("verified");
            });

            modelBuilder.Entity<AssetGroup>(entity =>
            {
                entity.ToTable("asset_groups");

                entity.HasIndex(e => e.Name, "name_unique")
                    .IsUnique();

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .HasColumnName("id");

                entity.Property(e => e.Name)
                    .HasMaxLength(100)
                    .HasColumnName("name");
            });

            modelBuilder.Entity<AssetValue>(entity =>
            {
                entity.ToTable("asset_values");

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .ValueGeneratedNever()
                    .HasColumnName("id");

                entity.Property(e => e.MaxValue)
                    .HasColumnType("int(11)")
                    .HasColumnName("max_value");

                entity.Property(e => e.MinValue)
                    .HasColumnType("int(11)")
                    .HasColumnName("min_value");

                entity.Property(e => e.ValuationLevelName)
                    .HasMaxLength(100)
                    .HasColumnName("valuation_level_name");
            });

            modelBuilder.Entity<AssetsAssetGroup>(entity =>
            {
                entity.HasNoKey();

                entity.ToTable("assets_asset_groups");

                entity.HasIndex(e => new { e.AssetId, e.AssetGroupId }, "asset_asset_group_unique")
                    .IsUnique();

                entity.Property(e => e.AssetGroupId)
                    .HasColumnType("int(11)")
                    .HasColumnName("asset_group_id");

                entity.Property(e => e.AssetId)
                    .HasColumnType("int(11)")
                    .HasColumnName("asset_id");
            });

            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.HasNoKey();

                entity.ToTable("audit_log");

                entity.Property(e => e.LogType)
                    .HasMaxLength(100)
                    .HasColumnName("log_type");

                entity.Property(e => e.Message)
                    .HasColumnType("mediumtext")
                    .HasColumnName("message");

                entity.Property(e => e.RiskId)
                    .HasColumnType("int(11)")
                    .HasColumnName("risk_id");

                entity.Property(e => e.Timestamp)
                    .HasColumnType("timestamp")
                    .HasColumnName("timestamp")
                    .HasDefaultValueSql("current_timestamp()");

                entity.Property(e => e.UserId)
                    .HasColumnType("int(11)")
                    .HasColumnName("user_id");
            });

            modelBuilder.Entity<Backup>(entity =>
            {
                entity.ToTable("backups");

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .HasColumnName("id");

                entity.Property(e => e.AppZipFileName)
                    .HasColumnType("mediumtext")
                    .HasColumnName("app_zip_file_name");

                entity.Property(e => e.DbZipFileName)
                    .HasColumnType("mediumtext")
                    .HasColumnName("db_zip_file_name");

                entity.Property(e => e.RandomId)
                    .HasMaxLength(50)
                    .HasColumnName("random_id");

                entity.Property(e => e.Timestamp)
                    .HasColumnType("timestamp")
                    .HasColumnName("timestamp")
                    .HasDefaultValueSql("current_timestamp()");
            });

            modelBuilder.Entity<Category>(entity =>
            {
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("category");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.Name)
                    .HasMaxLength(50)
                    .HasColumnName("name");
            });

            modelBuilder.Entity<CloseReason>(entity =>
            {
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("close_reason");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.Name)
                    .HasMaxLength(50)
                    .HasColumnName("name");
            });

            modelBuilder.Entity<Closure>(entity =>
            {
                entity.ToTable("closures");

                entity.HasIndex(e => e.CloseReason, "closures_close_reason_idx");

                entity.HasIndex(e => e.UserId, "closures_user_id_idx");

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .HasColumnName("id");

                entity.Property(e => e.CloseReason)
                    .HasColumnType("int(11)")
                    .HasColumnName("close_reason");

                entity.Property(e => e.ClosureDate)
                    .HasColumnType("timestamp")
                    .HasColumnName("closure_date")
                    .HasDefaultValueSql("current_timestamp()");

                entity.Property(e => e.Note)
                    .HasColumnType("mediumtext")
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
                entity.ToTable("comments");

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .HasColumnName("id");

                entity.Property(e => e.Comment1)
                    .HasColumnType("mediumtext")
                    .HasColumnName("comment");

                entity.Property(e => e.Date)
                    .HasColumnType("timestamp")
                    .HasColumnName("date")
                    .HasDefaultValueSql("current_timestamp()");

                entity.Property(e => e.RiskId)
                    .HasColumnType("int(11)")
                    .HasColumnName("risk_id");

                entity.Property(e => e.User)
                    .HasColumnType("int(11)")
                    .HasColumnName("user");
            });

            modelBuilder.Entity<ComplianceFile>(entity =>
            {
                entity.ToTable("compliance_files");

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .HasColumnName("id");

                entity.Property(e => e.Content).HasColumnName("content");

                entity.Property(e => e.Name)
                    .HasMaxLength(100)
                    .HasColumnName("name");

                entity.Property(e => e.RefId)
                    .HasColumnType("int(11)")
                    .HasColumnName("ref_id")
                    .HasDefaultValueSql("'0'");

                entity.Property(e => e.RefType)
                    .HasMaxLength(100)
                    .HasColumnName("ref_type")
                    .HasDefaultValueSql("''");

                entity.Property(e => e.Size)
                    .HasColumnType("int(11)")
                    .HasColumnName("size");

                entity.Property(e => e.Timestamp)
                    .HasColumnType("timestamp")
                    .HasColumnName("timestamp")
                    .HasDefaultValueSql("current_timestamp()");

                entity.Property(e => e.Type)
                    .HasMaxLength(128)
                    .HasColumnName("type");

                entity.Property(e => e.UniqueName)
                    .HasMaxLength(30)
                    .HasColumnName("unique_name");

                entity.Property(e => e.User)
                    .HasColumnType("int(11)")
                    .HasColumnName("user");

                entity.Property(e => e.Version)
                    .HasColumnType("int(11)")
                    .HasColumnName("version");
            });

            modelBuilder.Entity<ContributingRisk>(entity =>
            {
                entity.ToTable("contributing_risks");

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
                entity.ToTable("contributing_risks_impact");

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
                entity.ToTable("contributing_risks_likelihood");

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
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("control_class");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.Name).HasColumnName("name");
            });

            modelBuilder.Entity<ControlMaturity>(entity =>
            {
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("control_maturity");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .ValueGeneratedNever()
                    .HasColumnName("value");

                entity.Property(e => e.Name).HasColumnName("name");
            });

            modelBuilder.Entity<ControlPhase>(entity =>
            {
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("control_phase");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.Name).HasColumnName("name");
            });

            modelBuilder.Entity<ControlPriority>(entity =>
            {
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("control_priority");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.Name).HasColumnName("name");
            });

            modelBuilder.Entity<ControlType>(entity =>
            {
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("control_type");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.Name).HasColumnName("name");
            });

            modelBuilder.Entity<CustomRiskModelValue>(entity =>
            {
                entity.HasNoKey();

                entity.ToTable("custom_risk_model_values");

                entity.HasIndex(e => new { e.Impact, e.Likelihood }, "impact_likelihood_unique")
                    .IsUnique();

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

            modelBuilder.Entity<CvssScoring>(entity =>
            {
                entity.ToTable("CVSS_scoring");

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .HasColumnName("id");

                entity.Property(e => e.AbrvMetricName)
                    .HasMaxLength(3)
                    .HasColumnName("abrv_metric_name");

                entity.Property(e => e.AbrvMetricValue)
                    .HasMaxLength(3)
                    .HasColumnName("abrv_metric_value");

                entity.Property(e => e.MetricName)
                    .HasMaxLength(30)
                    .HasColumnName("metric_name");

                entity.Property(e => e.MetricValue)
                    .HasMaxLength(30)
                    .HasColumnName("metric_value");

                entity.Property(e => e.NumericValue).HasColumnName("numeric_value");
            });

            modelBuilder.Entity<DataClassification>(entity =>
            {
                entity.ToTable("data_classification");

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .HasColumnName("id");

                entity.Property(e => e.Name).HasColumnName("name");

                entity.Property(e => e.Order)
                    .HasColumnType("int(11)")
                    .HasColumnName("order");
            });

            modelBuilder.Entity<DateFormat>(entity =>
            {
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("date_formats");

                entity.Property(e => e.Value)
                    .HasMaxLength(20)
                    .HasColumnName("value");
            });

            modelBuilder.Entity<Document>(entity =>
            {
                entity.ToTable("documents");

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .HasColumnName("id");

                entity.Property(e => e.AdditionalStakeholders)
                    .HasMaxLength(500)
                    .HasColumnName("additional_stakeholders");

                entity.Property(e => e.ApprovalDate).HasColumnName("approval_date");

                entity.Property(e => e.Approver)
                    .HasColumnType("int(11)")
                    .HasColumnName("approver");

                entity.Property(e => e.ControlIds)
                    .HasMaxLength(500)
                    .HasColumnName("control_ids");

                entity.Property(e => e.CreationDate).HasColumnName("creation_date");

                entity.Property(e => e.DocumentName)
                    .HasColumnType("mediumtext")
                    .HasColumnName("document_name");

                entity.Property(e => e.DocumentOwner)
                    .HasColumnType("int(11)")
                    .HasColumnName("document_owner");

                entity.Property(e => e.DocumentStatus)
                    .HasColumnType("int(11)")
                    .HasColumnName("document_status")
                    .HasDefaultValueSql("'1'");

                entity.Property(e => e.DocumentType)
                    .HasMaxLength(50)
                    .HasColumnName("document_type");

                entity.Property(e => e.FileId)
                    .HasColumnType("int(11)")
                    .HasColumnName("file_id");

                entity.Property(e => e.FrameworkIds)
                    .HasMaxLength(500)
                    .HasColumnName("framework_ids");

                entity.Property(e => e.LastReviewDate).HasColumnName("last_review_date");

                entity.Property(e => e.NextReviewDate).HasColumnName("next_review_date");

                entity.Property(e => e.Parent)
                    .HasColumnType("int(11)")
                    .HasColumnName("parent");

                entity.Property(e => e.ReviewFrequency)
                    .HasColumnType("int(11)")
                    .HasColumnName("review_frequency");

                entity.Property(e => e.SubmittedBy)
                    .HasColumnType("int(11)")
                    .HasColumnName("submitted_by");

                entity.Property(e => e.TeamIds)
                    .HasMaxLength(500)
                    .HasColumnName("team_ids");

                entity.Property(e => e.UpdatedBy)
                    .HasColumnType("int(11)")
                    .HasColumnName("updated_by");
            });

            modelBuilder.Entity<DocumentException>(entity =>
            {
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("document_exceptions");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.AdditionalStakeholders)
                    .HasMaxLength(500)
                    .HasColumnName("additional_stakeholders");

                entity.Property(e => e.ApprovalDate)
                    .HasColumnName("approval_date")
                    .HasDefaultValueSql("'0000-00-00'");

                entity.Property(e => e.Approved).HasColumnName("approved");

                entity.Property(e => e.Approver)
                    .HasColumnType("int(11)")
                    .HasColumnName("approver");

                entity.Property(e => e.AssociatedRisks)
                    .HasColumnType("mediumtext")
                    .HasColumnName("associated_risks");

                entity.Property(e => e.ControlFrameworkId)
                    .HasColumnType("int(11)")
                    .HasColumnName("control_framework_id");

                entity.Property(e => e.CreationDate)
                    .HasColumnName("creation_date")
                    .HasDefaultValueSql("'0000-00-00'");

                entity.Property(e => e.Description)
                    .HasColumnType("blob")
                    .HasColumnName("description");

                entity.Property(e => e.FileId)
                    .HasColumnType("int(11)")
                    .HasColumnName("file_id");

                entity.Property(e => e.FrameworkId)
                    .HasColumnType("int(11)")
                    .HasColumnName("framework_id");

                entity.Property(e => e.Justification)
                    .HasColumnType("blob")
                    .HasColumnName("justification");

                entity.Property(e => e.Name)
                    .HasMaxLength(100)
                    .HasColumnName("name");

                entity.Property(e => e.NextReviewDate)
                    .HasColumnName("next_review_date")
                    .HasDefaultValueSql("'0000-00-00'");

                entity.Property(e => e.Owner)
                    .HasColumnType("int(11)")
                    .HasColumnName("owner");

                entity.Property(e => e.PolicyDocumentId)
                    .HasColumnType("int(11)")
                    .HasColumnName("policy_document_id");

                entity.Property(e => e.ReviewFrequency)
                    .HasColumnType("int(11)")
                    .HasColumnName("review_frequency");

                entity.Property(e => e.Status)
                    .HasColumnType("int(11)")
                    .HasColumnName("status")
                    .HasDefaultValueSql("'1'");
            });

            modelBuilder.Entity<DocumentExceptionsStatus>(entity =>
            {
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("document_exceptions_status");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.Name)
                    .HasMaxLength(100)
                    .HasColumnName("name");
            });

            modelBuilder.Entity<DocumentStatus>(entity =>
            {
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("document_status");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.Name)
                    .HasMaxLength(100)
                    .HasColumnName("name");
            });

            modelBuilder.Entity<DynamicSavedSelection>(entity =>
            {
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("dynamic_saved_selections");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.CustomColumnFilters)
                    .HasColumnType("mediumtext")
                    .HasColumnName("custom_column_filters");

                entity.Property(e => e.CustomDisplaySettings)
                    .HasMaxLength(1000)
                    .HasColumnName("custom_display_settings");

                entity.Property(e => e.CustomSelectionSettings)
                    .HasMaxLength(1000)
                    .HasColumnName("custom_selection_settings");

                entity.Property(e => e.Name)
                    .HasMaxLength(100)
                    .HasColumnName("name");

                entity.Property(e => e.Type)
                    .HasColumnType("enum('private','public')")
                    .HasColumnName("type");

                entity.Property(e => e.UserId)
                    .HasColumnType("int(11)")
                    .HasColumnName("user_id");
            });

            modelBuilder.Entity<EntitiesProperty>(entity =>
            {
                entity.ToTable("entities_properties");

                entity.HasIndex(e => e.Entity, "fk_entity");

                entity.HasIndex(e => e.Name, "idx_name");

                entity.HasIndex(e => e.Value, "idx_value")
                    .HasAnnotation("MySql:FullTextIndex", true);

                entity.Property(e => e.Id).HasColumnType("int(11)");

                entity.Property(e => e.Entity).HasColumnType("int(11)");

                entity.Property(e => e.OldValue).HasColumnType("text");

                entity.Property(e => e.Type).HasMaxLength(255);

                entity.Property(e => e.Value).HasColumnType("text");

                entity.HasOne(d => d.EntityNavigation)
                    .WithMany(p => p.EntitiesProperties)
                    .HasForeignKey(d => d.Entity)
                    .HasConstraintName("fk_entity");
            });

            modelBuilder.Entity<Entity>(entity =>
            {
                entity.ToTable("entities");

                entity.HasIndex(e => e.DefinitionName, "idx_definition_name");

                entity.Property(e => e.Id).HasColumnType("int(11)");

                entity.Property(e => e.Created)
                    .HasColumnType("timestamp")
                    .HasDefaultValueSql("current_timestamp()");

                entity.Property(e => e.CreatedBy).HasColumnType("int(11)");

                entity.Property(e => e.DefinitionVersion).HasMaxLength(15);

                entity.Property(e => e.Status).HasMaxLength(15);

                entity.Property(e => e.Updated)
                    .HasColumnType("timestamp")
                    .ValueGeneratedOnAddOrUpdate()
                    .HasDefaultValueSql("current_timestamp()");

                entity.Property(e => e.UpdatedBy).HasColumnType("int(11)");
            });

            modelBuilder.Entity<FailedLoginAttempt>(entity =>
            {
                entity.ToTable("failed_login_attempts");

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .HasColumnName("id");

                entity.Property(e => e.Date)
                    .HasColumnType("timestamp")
                    .HasColumnName("date")
                    .HasDefaultValueSql("current_timestamp()");

                entity.Property(e => e.Expired)
                    .HasColumnType("tinyint(4)")
                    .HasColumnName("expired")
                    .HasDefaultValueSql("'0'");

                entity.Property(e => e.Ip)
                    .HasMaxLength(15)
                    .HasColumnName("ip")
                    .HasDefaultValueSql("'0.0.0.0'");

                entity.Property(e => e.UserId)
                    .HasColumnType("int(11)")
                    .HasColumnName("user_id");
            });

            modelBuilder.Entity<Family>(entity =>
            {
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("family");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.Name)
                    .HasMaxLength(100)
                    .HasColumnName("name");
            });

            modelBuilder.Entity<Field>(entity =>
            {
                entity.ToTable("fields");

                entity.HasIndex(e => e.Name, "name")
                    .IsUnique();

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .HasColumnName("id");

                entity.Property(e => e.Name)
                    .HasMaxLength(100)
                    .HasColumnName("name");

                entity.Property(e => e.Type)
                    .HasMaxLength(20)
                    .HasColumnName("type");
            });

            modelBuilder.Entity<DAL.Entities.File>(entity =>
            {
                entity.ToTable("files");

                entity.HasIndex(e => e.UniqueName, "idx_unq_name")
                    .IsUnique();

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .HasColumnName("id");

                entity.Property(e => e.Content).HasColumnName("content");

                entity.Property(e => e.Name)
                    .HasMaxLength(100)
                    .HasColumnName("name");

                entity.Property(e => e.RiskId)
                    .HasColumnType("int(11)")
                    .HasColumnName("risk_id")
                    .HasDefaultValueSql("'0'");

                entity.Property(e => e.Size)
                    .HasColumnType("int(11)")
                    .HasColumnName("size");

                entity.Property(e => e.Timestamp)
                    .HasColumnType("timestamp")
                    .HasColumnName("timestamp")
                    .HasDefaultValueSql("current_timestamp()");

                entity.Property(e => e.Type)
                    .HasMaxLength(128)
                    .HasColumnName("type");

                entity.Property(e => e.UniqueName)
                    .HasMaxLength(128)
                    .HasColumnName("unique_name");

                entity.Property(e => e.User)
                    .HasColumnType("int(11)")
                    .HasColumnName("user");

                entity.Property(e => e.ViewType)
                    .HasColumnType("int(11)")
                    .HasColumnName("view_type")
                    .HasDefaultValueSql("'1'");
            });

            modelBuilder.Entity<FileType>(entity =>
            {
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("file_types");

                entity.HasIndex(e => e.Name, "name")
                    .IsUnique();

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.Name)
                    .HasMaxLength(250)
                    .HasColumnName("name");
            });

            modelBuilder.Entity<FileTypeExtension>(entity =>
            {
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("file_type_extensions");

                entity.HasIndex(e => e.Name, "name")
                    .IsUnique();

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.Name)
                    .HasMaxLength(10)
                    .HasColumnName("name");
            });

            modelBuilder.Entity<Framework>(entity =>
            {
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("frameworks");

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
                    .HasColumnType("int(11)")
                    .HasColumnName("status")
                    .HasDefaultValueSql("'1'");
            });

            modelBuilder.Entity<FrameworkControl>(entity =>
            {
                entity.ToTable("framework_controls");

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
                    .HasColumnName("control_status")
                    .HasDefaultValueSql("'1'");

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
                    .HasColumnType("int(11)")
                    .HasColumnName("status")
                    .HasDefaultValueSql("'1'");

                entity.Property(e => e.SubmissionDate)
                    .HasColumnType("timestamp")
                    .HasColumnName("submission_date")
                    .HasDefaultValueSql("current_timestamp()");

                entity.Property(e => e.SupplementalGuidance)
                    .HasColumnType("blob")
                    .HasColumnName("supplemental_guidance");
            });

            modelBuilder.Entity<FrameworkControlMapping>(entity =>
            {
                entity.ToTable("framework_control_mappings");

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
                    .HasMaxLength(1000)
                    .HasColumnName("reference_name");
            });

            modelBuilder.Entity<FrameworkControlTest>(entity =>
            {
                entity.ToTable("framework_control_tests");

                entity.HasIndex(e => e.Id, "id")
                    .IsUnique();

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

                entity.Property(e => e.ExpectedResults).HasColumnName("expected_results");

                entity.Property(e => e.FrameworkControlId)
                    .HasColumnType("int(11)")
                    .HasColumnName("framework_control_id");

                entity.Property(e => e.LastDate).HasColumnName("last_date");

                entity.Property(e => e.Name).HasColumnName("name");

                entity.Property(e => e.NextDate).HasColumnName("next_date");

                entity.Property(e => e.Objective).HasColumnName("objective");

                entity.Property(e => e.Status)
                    .HasColumnType("int(11)")
                    .HasColumnName("status")
                    .HasDefaultValueSql("'1'");

                entity.Property(e => e.TestFrequency)
                    .HasColumnType("int(11)")
                    .HasColumnName("test_frequency");

                entity.Property(e => e.TestSteps).HasColumnName("test_steps");

                entity.Property(e => e.Tester)
                    .HasColumnType("int(11)")
                    .HasColumnName("tester");
            });

            modelBuilder.Entity<FrameworkControlTestAudit>(entity =>
            {
                entity.ToTable("framework_control_test_audits");

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

                entity.Property(e => e.ExpectedResults).HasColumnName("expected_results");

                entity.Property(e => e.FrameworkControlId)
                    .HasColumnType("int(11)")
                    .HasColumnName("framework_control_id");

                entity.Property(e => e.LastDate).HasColumnName("last_date");

                entity.Property(e => e.Name).HasColumnName("name");

                entity.Property(e => e.NextDate).HasColumnName("next_date");

                entity.Property(e => e.Objective).HasColumnName("objective");

                entity.Property(e => e.Status)
                    .HasColumnType("int(11)")
                    .HasColumnName("status")
                    .HasDefaultValueSql("'1'");

                entity.Property(e => e.TestFrequency)
                    .HasColumnType("int(11)")
                    .HasColumnName("test_frequency");

                entity.Property(e => e.TestId)
                    .HasColumnType("int(11)")
                    .HasColumnName("test_id");

                entity.Property(e => e.TestSteps).HasColumnName("test_steps");

                entity.Property(e => e.Tester)
                    .HasColumnType("int(11)")
                    .HasColumnName("tester");
            });

            modelBuilder.Entity<FrameworkControlTestComment>(entity =>
            {
                entity.ToTable("framework_control_test_comments");

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .HasColumnName("id");

                entity.Property(e => e.Comment).HasColumnName("comment");

                entity.Property(e => e.Date)
                    .HasColumnType("timestamp")
                    .HasColumnName("date")
                    .HasDefaultValueSql("current_timestamp()");

                entity.Property(e => e.TestAuditId)
                    .HasColumnType("int(11)")
                    .HasColumnName("test_audit_id");

                entity.Property(e => e.User)
                    .HasColumnType("int(11)")
                    .HasColumnName("user");
            });

            modelBuilder.Entity<FrameworkControlTestResult>(entity =>
            {
                entity.ToTable("framework_control_test_results");

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .HasColumnName("id");

                entity.Property(e => e.LastUpdated)
                    .HasColumnType("timestamp")
                    .ValueGeneratedOnAddOrUpdate()
                    .HasColumnName("last_updated")
                    .HasDefaultValueSql("current_timestamp()");

                entity.Property(e => e.SubmissionDate)
                    .HasColumnType("datetime")
                    .HasColumnName("submission_date");

                entity.Property(e => e.SubmittedBy)
                    .HasColumnType("int(11)")
                    .HasColumnName("submitted_by");

                entity.Property(e => e.Summary)
                    .HasColumnType("mediumtext")
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
                entity.ToTable("framework_control_test_results_to_risks");

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

                entity.ToTable("framework_control_to_framework");

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
                entity.ToTable("framework_control_type_mappings");

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

            modelBuilder.Entity<GraphicalSavedSelection>(entity =>
            {
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("graphical_saved_selections");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.GraphicalDisplaySettings)
                    .HasMaxLength(1000)
                    .HasColumnName("graphical_display_settings");

                entity.Property(e => e.Name)
                    .HasMaxLength(100)
                    .HasColumnName("name");

                entity.Property(e => e.Type)
                    .HasColumnType("enum('private','public')")
                    .HasColumnName("type");

                entity.Property(e => e.UserId)
                    .HasColumnType("int(11)")
                    .HasColumnName("user_id");
            });

            modelBuilder.Entity<Impact>(entity =>
            {
                entity.HasNoKey();

                entity.ToTable("impact");

                entity.HasIndex(e => e.Value, "impact_index");

                entity.Property(e => e.Name)
                    .HasMaxLength(50)
                    .HasColumnName("name");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");
            });

            modelBuilder.Entity<ItemsToTeam>(entity =>
            {
                entity.HasNoKey();

                entity.ToTable("items_to_teams");

                entity.HasIndex(e => new { e.ItemId, e.TeamId, e.Type }, "item_team_unique")
                    .IsUnique();

                entity.HasIndex(e => new { e.ItemId, e.Type }, "item_type");

                entity.HasIndex(e => new { e.TeamId, e.Type }, "team_type");

                entity.HasIndex(e => e.Type, "type");

                entity.Property(e => e.ItemId)
                    .HasColumnType("int(11)")
                    .HasColumnName("item_id");

                entity.Property(e => e.TeamId)
                    .HasColumnType("int(11)")
                    .HasColumnName("team_id");

                entity.Property(e => e.Type)
                    .HasMaxLength(20)
                    .HasColumnName("type");
            });

            modelBuilder.Entity<Language>(entity =>
            {
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("languages");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.Full)
                    .HasMaxLength(50)
                    .HasColumnName("full");

                entity.Property(e => e.Name)
                    .HasMaxLength(5)
                    .HasColumnName("name");
            });

            modelBuilder.Entity<Likelihood>(entity =>
            {
                entity.HasNoKey();

                entity.ToTable("likelihood");

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
                entity.ToTable("links");

                entity.HasIndex(e => e.ExpirationDate, "expiration_date_idx");

                entity.HasIndex(e => new { e.KeyHash, e.Type }, "key_type_idx")
                    .IsUnique();

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .HasColumnName("id");

                entity.Property(e => e.CreationDate)
                    .HasColumnType("datetime")
                    .HasColumnName("creation_date")
                    .HasDefaultValueSql("current_timestamp()");

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
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("location");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.Name)
                    .HasMaxLength(100)
                    .HasColumnName("name");
            });

            modelBuilder.Entity<MgmtReview>(entity =>
            {
                entity.ToTable("mgmt_reviews");

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .HasColumnName("id");

                entity.Property(e => e.Comments)
                    .HasColumnType("mediumtext")
                    .HasColumnName("comments");

                entity.Property(e => e.NextReview)
                    .HasColumnName("next_review")
                    .HasDefaultValueSql("'0000-00-00'");

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
                    .HasColumnType("timestamp")
                    .HasColumnName("submission_date")
                    .HasDefaultValueSql("current_timestamp()");
            });

            modelBuilder.Entity<Mitigation>(entity =>
            {
                entity.ToTable("mitigations");

                entity.HasIndex(e => e.RiskId, "risk_id");

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .HasColumnName("id");

                entity.Property(e => e.CurrentSolution)
                    .HasColumnType("mediumtext")
                    .HasColumnName("current_solution");

                entity.Property(e => e.LastUpdate)
                    .HasColumnType("timestamp")
                    .HasColumnName("last_update")
                    .HasDefaultValueSql("'0000-00-00 00:00:00'");

                entity.Property(e => e.MitigationCost)
                    .HasColumnType("int(11)")
                    .HasColumnName("mitigation_cost")
                    .HasDefaultValueSql("'1'");

                entity.Property(e => e.MitigationEffort)
                    .HasColumnType("int(11)")
                    .HasColumnName("mitigation_effort");

                entity.Property(e => e.MitigationOwner)
                    .HasColumnType("int(11)")
                    .HasColumnName("mitigation_owner");

                entity.Property(e => e.MitigationPercent)
                    .HasColumnType("int(11)")
                    .HasColumnName("mitigation_percent");

                entity.Property(e => e.PlanningDate).HasColumnName("planning_date");

                entity.Property(e => e.PlanningStrategy)
                    .HasColumnType("int(11)")
                    .HasColumnName("planning_strategy");

                entity.Property(e => e.RiskId)
                    .HasColumnType("int(11)")
                    .HasColumnName("risk_id");

                entity.Property(e => e.SecurityRecommendations)
                    .HasColumnType("mediumtext")
                    .HasColumnName("security_recommendations");

                entity.Property(e => e.SecurityRequirements)
                    .HasColumnType("mediumtext")
                    .HasColumnName("security_requirements");

                entity.Property(e => e.SubmissionDate)
                    .HasColumnType("timestamp")
                    .HasColumnName("submission_date")
                    .HasDefaultValueSql("current_timestamp()");

                entity.Property(e => e.SubmittedBy)
                    .HasColumnType("int(11)")
                    .HasColumnName("submitted_by")
                    .HasDefaultValueSql("'1'");
            });

            modelBuilder.Entity<MitigationAcceptUser>(entity =>
            {
                entity.ToTable("mitigation_accept_users");

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
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

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
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("mitigation_effort");

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

                entity.ToTable("mitigation_to_controls");

                entity.HasIndex(e => new { e.ControlId, e.MitigationId }, "control_id");

                entity.HasIndex(e => e.ControlId, "mtg2ctrl_control_idx");

                entity.HasIndex(e => e.MitigationId, "mtg2ctrl_mtg_idx");

                entity.Property(e => e.MitigationId)
                    .HasColumnType("int(11)")
                    .HasColumnName("mitigation_id");

                entity.Property(e => e.ControlId)
                    .HasColumnType("int(11)")
                    .HasColumnName("control_id");

                entity.Property(e => e.ValidationDetails).HasColumnName("validation_details");

                entity.Property(e => e.ValidationMitigationPercent)
                    .HasColumnType("int(11)")
                    .HasColumnName("validation_mitigation_percent")
                    .HasDefaultValueSql("'0'");

                entity.Property(e => e.ValidationOwner)
                    .HasColumnType("int(11)")
                    .HasColumnName("validation_owner")
                    .HasDefaultValueSql("'0'");
            });

            modelBuilder.Entity<MitigationToTeam>(entity =>
            {
                entity.HasKey(e => new { e.MitigationId, e.TeamId })
                    .HasName("PRIMARY")
                    .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

                entity.ToTable("mitigation_to_team");

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
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("next_step");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.Name)
                    .HasMaxLength(50)
                    .HasColumnName("name");
            });

            modelBuilder.Entity<PasswordReset>(entity =>
            {
                entity.HasNoKey();

                entity.ToTable("password_reset");

                entity.Property(e => e.Attempts)
                    .HasColumnType("int(11)")
                    .HasColumnName("attempts");

                entity.Property(e => e.Timestamp)
                    .HasColumnType("timestamp")
                    .HasColumnName("timestamp")
                    .HasDefaultValueSql("current_timestamp()");

                entity.Property(e => e.Token)
                    .HasMaxLength(20)
                    .HasColumnName("token");

                entity.Property(e => e.Username)
                    .HasMaxLength(200)
                    .HasColumnName("username");
            });

            modelBuilder.Entity<PendingRisk>(entity =>
            {
                entity.ToTable("pending_risks");

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .HasColumnName("id");

                entity.Property(e => e.AffectedAssets)
                    .HasColumnType("mediumtext")
                    .HasColumnName("affected_assets");

                entity.Property(e => e.AssessmentAnswerId)
                    .HasColumnType("int(11)")
                    .HasColumnName("assessment_answer_id");

                entity.Property(e => e.AssessmentId)
                    .HasColumnType("int(11)")
                    .HasColumnName("assessment_id");

                entity.Property(e => e.Comment)
                    .HasColumnType("mediumtext")
                    .HasColumnName("comment");

                entity.Property(e => e.Owner)
                    .HasColumnType("int(11)")
                    .HasColumnName("owner");

                entity.Property(e => e.Score).HasColumnName("score");

                entity.Property(e => e.Subject)
                    .HasColumnType("blob")
                    .HasColumnName("subject");

                entity.Property(e => e.SubmissionDate)
                    .HasColumnType("timestamp")
                    .HasColumnName("submission_date")
                    .HasDefaultValueSql("current_timestamp()");
            });

            modelBuilder.Entity<Permission>(entity =>
            {
                entity.ToTable("permissions");

                entity.HasIndex(e => e.Key, "key")
                    .IsUnique();

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
            });

            modelBuilder.Entity<PermissionGroup>(entity =>
            {
                entity.ToTable("permission_groups");

                entity.HasIndex(e => e.Name, "name")
                    .IsUnique();

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

                entity.ToTable("permission_to_permission_group");

                entity.HasIndex(e => new { e.PermissionGroupId, e.PermissionId }, "permission_group_id");

                entity.Property(e => e.PermissionId)
                    .HasColumnType("int(11)")
                    .HasColumnName("permission_id");

                entity.Property(e => e.PermissionGroupId)
                    .HasColumnType("int(11)")
                    .HasColumnName("permission_group_id");
            });

            modelBuilder.Entity<PermissionToUser>(entity =>
            {
                entity.HasKey(e => new { e.PermissionId, e.UserId })
                    .HasName("PRIMARY")
                    .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

                entity.ToTable("permission_to_user");

                entity.HasIndex(e => new { e.UserId, e.PermissionId }, "user_id");

                entity.Property(e => e.PermissionId)
                    .HasColumnType("int(11)")
                    .HasColumnName("permission_id");

                entity.Property(e => e.UserId)
                    .HasColumnType("int(11)")
                    .HasColumnName("user_id");
            });

            modelBuilder.Entity<PlanningStrategy>(entity =>
            {
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("planning_strategy");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.Name)
                    .HasMaxLength(20)
                    .HasColumnName("name");
            });

            modelBuilder.Entity<Project>(entity =>
            {
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("projects");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.BusinessOwner)
                    .HasColumnType("int(11)")
                    .HasColumnName("business_owner");

                entity.Property(e => e.Consultant)
                    .HasColumnType("int(11)")
                    .HasColumnName("consultant");

                entity.Property(e => e.DataClassification)
                    .HasColumnType("int(11)")
                    .HasColumnName("data_classification");

                entity.Property(e => e.DueDate)
                    .HasColumnType("timestamp")
                    .HasColumnName("due_date");

                entity.Property(e => e.Name)
                    .HasMaxLength(100)
                    .HasColumnName("name");

                entity.Property(e => e.Order)
                    .HasColumnType("int(11)")
                    .HasColumnName("order")
                    .HasDefaultValueSql("'999999'");

                entity.Property(e => e.Status)
                    .HasColumnType("int(11)")
                    .HasColumnName("status")
                    .HasDefaultValueSql("'1'");
            });

            modelBuilder.Entity<Regulation>(entity =>
            {
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("regulation");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.Name)
                    .HasMaxLength(50)
                    .HasColumnName("name");
            });

            modelBuilder.Entity<ResidualRiskScoringHistory>(entity =>
            {
                entity.ToTable("residual_risk_scoring_history");

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
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("review");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.Name)
                    .HasMaxLength(50)
                    .HasColumnName("name");
            });

            modelBuilder.Entity<ReviewLevel>(entity =>
            {
                entity.HasNoKey();

                entity.ToTable("review_levels");

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
                entity.ToTable("risks");

                entity.HasIndex(e => e.Category, "category");

                entity.HasIndex(e => e.CloseId, "close_id");

                entity.HasIndex(e => e.Manager, "manager");

                entity.HasIndex(e => e.MgmtReview, "mgmt_review");

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
                    .HasColumnType("timestamp")
                    .HasColumnName("last_update")
                    .HasDefaultValueSql("'0000-00-00 00:00:00'");

                entity.Property(e => e.Manager)
                    .HasColumnType("int(11)")
                    .HasColumnName("manager");

                entity.Property(e => e.MgmtReview)
                    .HasColumnType("int(11)")
                    .HasColumnName("mgmt_review")
                    .HasDefaultValueSql("'0'");

                entity.Property(e => e.MitigationId)
                    .HasColumnType("int(11)")
                    .HasColumnName("mitigation_id")
                    .HasDefaultValueSql("'0'");

                entity.Property(e => e.Notes).HasColumnName("notes");

                entity.Property(e => e.Owner)
                    .HasColumnType("int(11)")
                    .HasColumnName("owner");

                entity.Property(e => e.ProjectId)
                    .HasColumnType("int(11)")
                    .HasColumnName("project_id");

                entity.Property(e => e.ReferenceId)
                    .HasMaxLength(20)
                    .HasColumnName("reference_id")
                    .HasDefaultValueSql("''");

                entity.Property(e => e.Regulation)
                    .HasColumnType("int(11)")
                    .HasColumnName("regulation");

                entity.Property(e => e.ReviewDate)
                    .HasColumnType("timestamp")
                    .HasColumnName("review_date")
                    .HasDefaultValueSql("'0000-00-00 00:00:00'");

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
                    .HasColumnType("timestamp")
                    .HasColumnName("submission_date")
                    .HasDefaultValueSql("current_timestamp()");

                entity.Property(e => e.SubmittedBy)
                    .HasColumnType("int(11)")
                    .HasColumnName("submitted_by")
                    .HasDefaultValueSql("'1'");

                entity.Property(e => e.TemplateGroupId)
                    .HasColumnType("int(11)")
                    .HasColumnName("template_group_id")
                    .HasDefaultValueSql("'1'");

                entity.Property(e => e.ThreatCatalogMapping)
                    .HasMaxLength(255)
                    .HasColumnName("threat_catalog_mapping");
            });

            modelBuilder.Entity<RiskCatalog>(entity =>
            {
                entity.ToTable("risk_catalog");

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .HasColumnName("id");

                entity.Property(e => e.Description)
                    .HasColumnType("mediumtext")
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
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("risk_function");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.Name)
                    .HasMaxLength(50)
                    .HasColumnName("name");
            });

            modelBuilder.Entity<RiskGrouping>(entity =>
            {
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("risk_grouping");

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
                entity.HasNoKey();

                entity.ToTable("risk_levels");

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

            modelBuilder.Entity<RiskModel>(entity =>
            {
                entity.HasNoKey();

                entity.ToTable("risk_models");

                entity.Property(e => e.Name)
                    .HasMaxLength(50)
                    .HasColumnName("name");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");
            });

            modelBuilder.Entity<RiskScoring>(entity =>
            {
                entity.ToTable("risk_scoring");

                entity.HasIndex(e => e.CalculatedRisk, "calculated_risk");

                entity.HasIndex(e => e.Id, "id")
                    .IsUnique();

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .ValueGeneratedNever()
                    .HasColumnName("id");

                entity.Property(e => e.CalculatedRisk).HasColumnName("calculated_risk");

                entity.Property(e => e.ClassicImpact)
                    .HasColumnName("CLASSIC_impact")
                    .HasDefaultValueSql("'5'");

                entity.Property(e => e.ClassicLikelihood)
                    .HasColumnName("CLASSIC_likelihood")
                    .HasDefaultValueSql("'5'");

                entity.Property(e => e.ContributingLikelihood)
                    .HasColumnType("int(11)")
                    .HasColumnName("Contributing_Likelihood")
                    .HasDefaultValueSql("'0'");

                entity.Property(e => e.Custom).HasDefaultValueSql("'10'");

                entity.Property(e => e.CvssAccessComplexity)
                    .HasMaxLength(3)
                    .HasColumnName("CVSS_AccessComplexity")
                    .HasDefaultValueSql("'L'");

                entity.Property(e => e.CvssAccessVector)
                    .HasMaxLength(3)
                    .HasColumnName("CVSS_AccessVector")
                    .HasDefaultValueSql("'N'");

                entity.Property(e => e.CvssAuthentication)
                    .HasMaxLength(3)
                    .HasColumnName("CVSS_Authentication")
                    .HasDefaultValueSql("'N'");

                entity.Property(e => e.CvssAvailImpact)
                    .HasMaxLength(3)
                    .HasColumnName("CVSS_AvailImpact")
                    .HasDefaultValueSql("'C'");

                entity.Property(e => e.CvssAvailabilityRequirement)
                    .HasMaxLength(3)
                    .HasColumnName("CVSS_AvailabilityRequirement")
                    .HasDefaultValueSql("'ND'");

                entity.Property(e => e.CvssCollateralDamagePotential)
                    .HasMaxLength(3)
                    .HasColumnName("CVSS_CollateralDamagePotential")
                    .HasDefaultValueSql("'ND'");

                entity.Property(e => e.CvssConfImpact)
                    .HasMaxLength(3)
                    .HasColumnName("CVSS_ConfImpact")
                    .HasDefaultValueSql("'C'");

                entity.Property(e => e.CvssConfidentialityRequirement)
                    .HasMaxLength(3)
                    .HasColumnName("CVSS_ConfidentialityRequirement")
                    .HasDefaultValueSql("'ND'");

                entity.Property(e => e.CvssExploitability)
                    .HasMaxLength(3)
                    .HasColumnName("CVSS_Exploitability")
                    .HasDefaultValueSql("'ND'");

                entity.Property(e => e.CvssIntegImpact)
                    .HasMaxLength(3)
                    .HasColumnName("CVSS_IntegImpact")
                    .HasDefaultValueSql("'C'");

                entity.Property(e => e.CvssIntegrityRequirement)
                    .HasMaxLength(3)
                    .HasColumnName("CVSS_IntegrityRequirement")
                    .HasDefaultValueSql("'ND'");

                entity.Property(e => e.CvssRemediationLevel)
                    .HasMaxLength(3)
                    .HasColumnName("CVSS_RemediationLevel")
                    .HasDefaultValueSql("'ND'");

                entity.Property(e => e.CvssReportConfidence)
                    .HasMaxLength(3)
                    .HasColumnName("CVSS_ReportConfidence")
                    .HasDefaultValueSql("'ND'");

                entity.Property(e => e.CvssTargetDistribution)
                    .HasMaxLength(3)
                    .HasColumnName("CVSS_TargetDistribution")
                    .HasDefaultValueSql("'ND'");

                entity.Property(e => e.DreadAffectedUsers)
                    .HasColumnType("int(11)")
                    .HasColumnName("DREAD_AffectedUsers")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.DreadDamagePotential)
                    .HasColumnType("int(11)")
                    .HasColumnName("DREAD_DamagePotential")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.DreadDiscoverability)
                    .HasColumnType("int(11)")
                    .HasColumnName("DREAD_Discoverability")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.DreadExploitability)
                    .HasColumnType("int(11)")
                    .HasColumnName("DREAD_Exploitability")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.DreadReproducibility)
                    .HasColumnType("int(11)")
                    .HasColumnName("DREAD_Reproducibility")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspAwareness)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_Awareness")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspEaseOfDiscovery)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_EaseOfDiscovery")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspEaseOfExploit)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_EaseOfExploit")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspFinancialDamage)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_FinancialDamage")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspIntrusionDetection)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_IntrusionDetection")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspLossOfAccountability)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_LossOfAccountability")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspLossOfAvailability)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_LossOfAvailability")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspLossOfConfidentiality)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_LossOfConfidentiality")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspLossOfIntegrity)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_LossOfIntegrity")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspMotive)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_Motive")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspNonCompliance)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_NonCompliance")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspOpportunity)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_Opportunity")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspPrivacyViolation)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_PrivacyViolation")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspReputationDamage)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_ReputationDamage")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspSize)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_Size")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.OwaspSkillLevel)
                    .HasColumnType("int(11)")
                    .HasColumnName("OWASP_SkillLevel")
                    .HasDefaultValueSql("'10'");

                entity.Property(e => e.ScoringMethod)
                    .HasColumnType("int(11)")
                    .HasColumnName("scoring_method");
            });

            modelBuilder.Entity<RiskScoringContributingImpact>(entity =>
            {
                entity.ToTable("risk_scoring_contributing_impacts");

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
                entity.ToTable("risk_scoring_history");

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

                entity.ToTable("risk_to_additional_stakeholder");

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

                entity.ToTable("risk_to_location");

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

                entity.ToTable("risk_to_team");

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

                entity.ToTable("risk_to_technology");

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
                entity.HasNoKey();

                entity.ToTable("risks_to_assets");

                entity.HasIndex(e => new { e.AssetId, e.RiskId }, "asset_id")
                    .IsUnique();

                entity.Property(e => e.AssetId)
                    .HasColumnType("int(11)")
                    .HasColumnName("asset_id");

                entity.Property(e => e.RiskId)
                    .HasColumnType("int(11)")
                    .HasColumnName("risk_id");
            });

            modelBuilder.Entity<RisksToAssetGroup>(entity =>
            {
                entity.HasNoKey();

                entity.ToTable("risks_to_asset_groups");

                entity.HasIndex(e => new { e.AssetGroupId, e.RiskId }, "asset_group_id")
                    .IsUnique();

                entity.Property(e => e.AssetGroupId)
                    .HasColumnType("int(11)")
                    .HasColumnName("asset_group_id");

                entity.Property(e => e.RiskId)
                    .HasColumnType("int(11)")
                    .HasColumnName("risk_id");
            });

            modelBuilder.Entity<Role>(entity =>
            {
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("role");

                entity.HasIndex(e => e.Default, "default")
                    .IsUnique();

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.Admin).HasColumnName("admin");

                entity.Property(e => e.Default).HasColumnName("default");

                entity.Property(e => e.Name)
                    .HasMaxLength(100)
                    .HasColumnName("name");
            });

            modelBuilder.Entity<RoleResponsibility>(entity =>
            {
                entity.HasKey(e => new { e.RoleId, e.PermissionId })
                    .HasName("PRIMARY")
                    .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

                entity.ToTable("role_responsibilities");

                entity.HasIndex(e => new { e.PermissionId, e.RoleId }, "permission_id");

                entity.Property(e => e.RoleId)
                    .HasColumnType("int(11)")
                    .HasColumnName("role_id");

                entity.Property(e => e.PermissionId)
                    .HasColumnType("int(11)")
                    .HasColumnName("permission_id");
            });

            modelBuilder.Entity<SavedTableDisplaySetting>(entity =>
            {
                entity.ToTable("saved_table_display_settings");

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .HasColumnName("id");

                entity.Property(e => e.DisplaySettings)
                    .HasColumnType("mediumtext")
                    .HasColumnName("display_settings");

                entity.Property(e => e.Name)
                    .HasMaxLength(100)
                    .HasColumnName("name")
                    .HasComment("Name of the save. Only used if there are multiple saves for the same view.");

                entity.Property(e => e.UserId)
                    .HasColumnType("int(11)")
                    .HasColumnName("user_id")
                    .HasComment("ID of the user who created the save");

                entity.Property(e => e.View)
                    .HasMaxLength(100)
                    .HasColumnName("view")
                    .HasComment("Name of the view like plan_mitigation or asset_edit to be able to get it for the table where it is used");

                entity.Property(e => e.Visibility)
                    .HasColumnType("enum('private','public')")
                    .HasColumnName("visibility")
                    .HasDefaultValueSql("'private'")
                    .HasComment("Visibility of the save. Only used if there are multiple saves for the same view.");
            });

            modelBuilder.Entity<ScoringMethod>(entity =>
            {
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("scoring_methods");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.Name)
                    .HasMaxLength(20)
                    .HasColumnName("name");
            });

            modelBuilder.Entity<Session>(entity =>
            {
                entity.ToTable("sessions");

                entity.Property(e => e.Id)
                    .HasMaxLength(128)
                    .HasColumnName("id");

                entity.Property(e => e.Access)
                    .HasColumnType("int(10) unsigned")
                    .HasColumnName("access");

                entity.Property(e => e.Data)
                    .HasColumnType("blob")
                    .HasColumnName("data");
            });

            modelBuilder.Entity<Setting>(entity =>
            {
                entity.HasKey(e => e.Name)
                    .HasName("PRIMARY");

                entity.ToTable("settings");

                entity.Property(e => e.Name)
                    .HasMaxLength(100)
                    .HasColumnName("name")
                    .HasDefaultValueSql("''");

                entity.Property(e => e.Value).HasColumnName("value");
            });

            modelBuilder.Entity<Source>(entity =>
            {
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("source");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.Name)
                    .HasMaxLength(50)
                    .HasColumnName("name");
            });

            modelBuilder.Entity<Status>(entity =>
            {
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("status");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.Name)
                    .HasMaxLength(50)
                    .HasColumnName("name");
            });

            modelBuilder.Entity<Tag>(entity =>
            {
                entity.ToTable("tags");

                entity.HasIndex(e => e.Tag1, "tag_unique")
                    .IsUnique();

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .HasColumnName("id");

                entity.Property(e => e.Tag1)
                    .HasMaxLength(500)
                    .HasColumnName("tag");
            });

            modelBuilder.Entity<TagsTaggee>(entity =>
            {
                entity.HasNoKey();

                entity.ToTable("tags_taggees");

                entity.HasIndex(e => new { e.TagId, e.TaggeeId, e.Type }, "tag_taggee_unique")
                    .IsUnique();

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
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("team");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.Name)
                    .HasMaxLength(50)
                    .HasColumnName("name");
            });

            modelBuilder.Entity<Technology>(entity =>
            {
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("technology");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.Name)
                    .HasMaxLength(50)
                    .HasColumnName("name");
            });

            modelBuilder.Entity<TestResult>(entity =>
            {
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("test_results");

                entity.HasIndex(e => e.Name, "name_unique")
                    .IsUnique();

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
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("test_status");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.Name)
                    .HasMaxLength(100)
                    .HasColumnName("name");
            });

            modelBuilder.Entity<ThreatCatalog>(entity =>
            {
                entity.ToTable("threat_catalog");

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .HasColumnName("id");

                entity.Property(e => e.Description)
                    .HasColumnType("mediumtext")
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
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("threat_grouping");

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
                entity.HasKey(e => e.Value)
                    .HasName("PRIMARY");

                entity.ToTable("user");

                entity.Property(e => e.Value)
                    .HasColumnType("int(11)")
                    .HasColumnName("value");

                entity.Property(e => e.Admin).HasColumnName("admin");

                entity.Property(e => e.ChangePassword)
                    .HasColumnType("tinyint(4)")
                    .HasColumnName("change_password");

                entity.Property(e => e.CustomDisplaySettings)
                    .HasMaxLength(1000)
                    .HasColumnName("custom_display_settings")
                    .HasDefaultValueSql("''");

                entity.Property(e => e.CustomPerformReviewsDisplaySettings)
                    .HasMaxLength(2000)
                    .HasColumnName("custom_perform_reviews_display_settings")
                    .HasDefaultValueSql("'{\"risk_colums\":[[\"id\",\"1\"],[\"risk_status\",\"1\"],[\"subject\",\"1\"],[\"calculated_risk\",\"1\"],[\"submission_date\",\"1\"]],\"mitigation_colums\":[[\"mitigation_planned\",\"1\"]],\"review_colums\":[[\"management_review\",\"1\"]]}\\\\n'");

                entity.Property(e => e.CustomPlanMitigationDisplaySettings)
                    .HasMaxLength(2000)
                    .HasColumnName("custom_plan_mitigation_display_settings")
                    .HasDefaultValueSql("'{\"risk_colums\":[[\"id\",\"1\"],[\"risk_status\",\"1\"],[\"subject\",\"1\"],[\"calculated_risk\",\"1\"],[\"submission_date\",\"1\"]],\"mitigation_colums\":[[\"mitigation_planned\",\"1\"]],\"review_colums\":[[\"management_review\",\"1\"]]}\\\\n'");

                entity.Property(e => e.CustomReviewregularlyDisplaySettings)
                    .HasMaxLength(2000)
                    .HasColumnName("custom_reviewregularly_display_settings")
                    .HasDefaultValueSql("'{\"risk_colums\":[[\"id\",\"1\"],[\"risk_status\",\"1\"],[\"subject\",\"1\"],[\"calculated_risk\",\"1\"],[\"days_open\",\"1\"]],\"review_colums\":[[\"management_review\",\"0\"],[\"review_date\",\"0\"],[\"next_step\",\"0\"],[\"next_review_date\",\"1\"],[\"comments\",\"0\"]]}'");

                entity.Property(e => e.CustomRisksAndIssuesSettings)
                    .HasMaxLength(2000)
                    .HasColumnName("custom_risks_and_issues_settings");

                entity.Property(e => e.Email)
                    .HasColumnType("blob")
                    .HasColumnName("email");

                entity.Property(e => e.Enabled)
                    .IsRequired()
                    .HasColumnName("enabled")
                    .HasDefaultValueSql("'1'");

                entity.Property(e => e.Lang)
                    .HasMaxLength(5)
                    .HasColumnName("lang");

                entity.Property(e => e.LastLogin)
                    .HasColumnType("datetime")
                    .HasColumnName("last_login");

                entity.Property(e => e.LastPasswordChangeDate)
                    .HasColumnType("timestamp")
                    .HasColumnName("last_password_change_date")
                    .HasDefaultValueSql("current_timestamp()");

                entity.Property(e => e.Lockout)
                    .HasColumnType("tinyint(4)")
                    .HasColumnName("lockout");

                entity.Property(e => e.Manager)
                    .HasColumnType("int(11)")
                    .HasColumnName("manager");

                entity.Property(e => e.MultiFactor)
                    .HasColumnType("int(11)")
                    .HasColumnName("multi_factor");

                entity.Property(e => e.Name)
                    .HasMaxLength(50)
                    .HasColumnName("name");

                entity.Property(e => e.Password)
                    .HasMaxLength(60)
                    .HasColumnName("password")
                    .IsFixedLength();

                entity.Property(e => e.RoleId)
                    .HasColumnType("int(11)")
                    .HasColumnName("role_id");

                entity.Property(e => e.Salt)
                    .HasMaxLength(20)
                    .HasColumnName("salt");

                entity.Property(e => e.Type)
                    .HasMaxLength(20)
                    .HasColumnName("type")
                    .HasDefaultValueSql("'simplerisk'");

                entity.Property(e => e.Username)
                    .HasColumnType("blob")
                    .HasColumnName("username");
            });

            modelBuilder.Entity<UserMfa>(entity =>
            {
                entity.HasKey(e => e.Uid)
                    .HasName("PRIMARY");

                entity.ToTable("user_mfa");

                entity.Property(e => e.Uid)
                    .HasColumnType("int(11)")
                    .ValueGeneratedNever()
                    .HasColumnName("uid");

                entity.Property(e => e.Secret)
                    .HasMaxLength(16)
                    .HasColumnName("secret");

                entity.Property(e => e.Verified)
                    .HasColumnType("int(11)")
                    .HasColumnName("verified")
                    .HasDefaultValueSql("'0'");
            });

            modelBuilder.Entity<UserPassHistory>(entity =>
            {
                entity.ToTable("user_pass_history");

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .HasColumnName("id");

                entity.Property(e => e.AddDate)
                    .HasColumnType("timestamp")
                    .HasColumnName("add_date")
                    .HasDefaultValueSql("current_timestamp()");

                entity.Property(e => e.Password)
                    .HasMaxLength(60)
                    .HasColumnName("password")
                    .IsFixedLength();

                entity.Property(e => e.Salt)
                    .HasMaxLength(20)
                    .HasColumnName("salt");

                entity.Property(e => e.UserId)
                    .HasColumnType("int(11)")
                    .HasColumnName("user_id");
            });

            modelBuilder.Entity<UserPassReuseHistory>(entity =>
            {
                entity.ToTable("user_pass_reuse_history");

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .HasColumnName("id");

                entity.Property(e => e.Counts)
                    .HasColumnType("int(11)")
                    .HasColumnName("counts")
                    .HasDefaultValueSql("'1'");

                entity.Property(e => e.Password)
                    .HasMaxLength(60)
                    .HasColumnName("password")
                    .IsFixedLength();

                entity.Property(e => e.UserId)
                    .HasColumnType("int(11)")
                    .HasColumnName("user_id");
            });

            modelBuilder.Entity<UserToTeam>(entity =>
            {
                entity.HasKey(e => new { e.UserId, e.TeamId })
                    .HasName("PRIMARY")
                    .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

                entity.ToTable("user_to_team");

                entity.HasIndex(e => new { e.TeamId, e.UserId }, "team_id");

                entity.Property(e => e.UserId)
                    .HasColumnType("int(11)")
                    .HasColumnName("user_id");

                entity.Property(e => e.TeamId)
                    .HasColumnType("int(11)")
                    .HasColumnName("team_id");
            });

            modelBuilder.Entity<ValidationFile>(entity =>
            {
                entity.ToTable("validation_files");

                entity.Property(e => e.Id)
                    .HasColumnType("int(11)")
                    .HasColumnName("id");

                entity.Property(e => e.Content).HasColumnName("content");

                entity.Property(e => e.ControlId)
                    .HasColumnType("int(11)")
                    .HasColumnName("control_id");

                entity.Property(e => e.MitigationId)
                    .HasColumnType("int(11)")
                    .HasColumnName("mitigation_id");

                entity.Property(e => e.Name)
                    .HasMaxLength(100)
                    .HasColumnName("name");

                entity.Property(e => e.Size)
                    .HasColumnType("int(11)")
                    .HasColumnName("size");

                entity.Property(e => e.Timestamp)
                    .HasColumnType("timestamp")
                    .HasColumnName("timestamp")
                    .HasDefaultValueSql("current_timestamp()");

                entity.Property(e => e.Type)
                    .HasMaxLength(30)
                    .HasColumnName("type");

                entity.Property(e => e.User)
                    .HasColumnType("int(11)")
                    .HasColumnName("user");
            });

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
