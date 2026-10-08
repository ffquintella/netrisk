using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// Stage 9.10 — the third-party register (S51 §4): <c>third_parties</c> (the supplier as a first-class record, with its
/// contract, SLA, contracted RTO/RPO, right to audit and exit plan), <c>third_party_links</c> (what it supplies or
/// processes: an IT service, a business process, a data record), <c>third_party_subprocessors</c>,
/// <c>third_party_data_locations</c>, <c>third_party_assessments</c> and <c>third_party_assessment_answers</c> (the
/// HECVAT), and <c>third_party_sboms</c> and <c>third_party_sbom_components</c> (the SBOM of what it supplies).
///
/// Named per the Track 6 convention: snake_case columns via <c>HasColumnName</c>, <c>fk_</c>/<c>idx_</c>/<c>uq_</c>/
/// <c>ck_</c> prefixes, UTC <c>datetime</c>, <c>int</c> + <c>HasConversion</c> for the enums, <c>tinyint(1)</c> for the
/// booleans, <c>varchar(n)</c> and never <c>char(n)</c> — the country codes are <c>varchar(2)</c> —, <c>text</c> for the
/// exit plan. Every CHECK is declared on the model too, so the snapshot carries the same DDL as <c>Structure/99.sql</c>,
/// and no CHECK names a column a <c>SET NULL</c> foreign key may clear.
///
/// <b>A third party in use cannot be deleted</b> (S51 §4.9): the links, the assessments, the SBOMs and the rows naming it
/// a sub-processor reference it with <c>RESTRICT</c>, so the database refuses what <c>ThirdPartyReferences</c> refuses
/// first. Its own declarations — its sub-processors and data locations — go with it. No inverse navigation on
/// <see cref="Entity"/> or <see cref="User"/>, so the payloads of <c>GET /Entities</c> and <c>GET /Users</c> do not
/// change. The query filters are in <c>NRDbContext.EntityScope.cs</c>.
/// </summary>
public partial class NRDbContext
{
    public virtual DbSet<ThirdParty> ThirdParties { get; set; } = null!;

    public virtual DbSet<ThirdPartyLink> ThirdPartyLinks { get; set; } = null!;

    public virtual DbSet<ThirdPartySubprocessor> ThirdPartySubprocessors { get; set; } = null!;

    public virtual DbSet<ThirdPartyDataLocation> ThirdPartyDataLocations { get; set; } = null!;

    public virtual DbSet<ThirdPartyAssessment> ThirdPartyAssessments { get; set; } = null!;

    public virtual DbSet<ThirdPartyAssessmentAnswer> ThirdPartyAssessmentAnswers { get; set; } = null!;

    public virtual DbSet<ThirdPartySbom> ThirdPartySboms { get; set; } = null!;

    public virtual DbSet<ThirdPartySbomComponent> ThirdPartySbomComponents { get; set; } = null!;

    private static void ConfigureThirdParties(ModelBuilder modelBuilder)
    {
        ConfigureThirdPartyRecord(modelBuilder);
        ConfigureThirdPartyDeclarations(modelBuilder);
        ConfigureThirdPartyAssessments(modelBuilder);
        ConfigureThirdPartySboms(modelBuilder);
    }

    private static void ConfigureThirdPartyRecord(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ThirdParty>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("third_parties", t =>
                {
                    t.HasCheckConstraint("ck_third_parties_status", "`status` >= 1 AND `status` <= 4");
                    t.HasCheckConstraint("ck_third_parties_terminated",
                        "(`status` = 4 AND `terminated_at` IS NOT NULL) OR (`status` <> 4 AND `terminated_at` IS NULL)");
                    t.HasCheckConstraint("ck_third_parties_sla_availability",
                        "`sla_availability_percent` IS NULL OR (`sla_availability_percent` > 0 AND `sla_availability_percent` <= 100)");
                    t.HasCheckConstraint("ck_third_parties_non_negative",
                        "(`contracted_rto_minutes` IS NULL OR `contracted_rto_minutes` >= 0) AND (`contracted_rpo_minutes` IS NULL OR `contracted_rpo_minutes` >= 0) AND (`vulnerability_fix_days` IS NULL OR `vulnerability_fix_days` >= 0)");
                    t.HasCheckConstraint("ck_third_parties_contract_dates",
                        "`contract_start` IS NULL OR `contract_end` IS NULL OR `contract_end` >= `contract_start`");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.Name).HasColumnName("name").HasColumnType("varchar(200)").HasMaxLength(200);
            entity.Property(e => e.LegalName).HasColumnName("legal_name").HasColumnType("varchar(300)").HasMaxLength(300);
            entity.Property(e => e.TaxId).HasColumnName("tax_id").HasColumnType("varchar(50)").HasMaxLength(50);
            entity.Property(e => e.Country).HasColumnName("country").HasColumnType("varchar(2)").HasMaxLength(2);
            entity.Property(e => e.Description).HasColumnName("description").HasColumnType("varchar(2000)")
                .HasMaxLength(2000);
            entity.Property(e => e.Website).HasColumnName("website").HasColumnType("varchar(500)").HasMaxLength(500);
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.OwnerId).HasColumnName("owner_id").HasColumnType("int(11)");
            entity.Property(e => e.Status).HasColumnName("status").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.IsCloudProvider).HasColumnName("is_cloud_provider").HasColumnType("tinyint(1)");
            entity.Property(e => e.IsIdentityProvider).HasColumnName("is_identity_provider").HasColumnType("tinyint(1)");
            entity.Property(e => e.ProcessesPersonalData).HasColumnName("processes_personal_data")
                .HasColumnType("tinyint(1)");
            entity.Property(e => e.SubprocessorsDeclaredAt).HasColumnName("subprocessors_declared_at")
                .HasColumnType("datetime");
            entity.Property(e => e.ContractReference).HasColumnName("contract_reference").HasColumnType("varchar(200)")
                .HasMaxLength(200);
            entity.Property(e => e.ContractStart).HasColumnName("contract_start").HasColumnType("datetime");
            entity.Property(e => e.ContractEnd).HasColumnName("contract_end").HasColumnType("datetime");
            entity.Property(e => e.SlaAvailabilityPercent).HasColumnName("sla_availability_percent")
                .HasColumnType("decimal(6,3)").HasPrecision(6, 3);
            entity.Property(e => e.ContractedRtoMinutes).HasColumnName("contracted_rto_minutes").HasColumnType("int(11)");
            entity.Property(e => e.ContractedRpoMinutes).HasColumnName("contracted_rpo_minutes").HasColumnType("int(11)");
            entity.Property(e => e.VulnerabilityFixDays).HasColumnName("vulnerability_fix_days").HasColumnType("int(11)");
            entity.Property(e => e.RightToAudit).HasColumnName("right_to_audit").HasColumnType("tinyint(1)");
            entity.Property(e => e.AuditClauseReference).HasColumnName("audit_clause_reference")
                .HasColumnType("varchar(500)").HasMaxLength(500);
            entity.Property(e => e.ExitPlan).HasColumnName("exit_plan").HasColumnType("text");
            entity.Property(e => e.ExitPlanReviewedAt).HasColumnName("exit_plan_reviewed_at").HasColumnType("datetime");
            entity.Property(e => e.ExitPlanTestedAt).HasColumnName("exit_plan_tested_at").HasColumnType("datetime");
            entity.Property(e => e.DataPortability).HasColumnName("data_portability").HasColumnType("varchar(2000)")
                .HasMaxLength(2000);
            entity.Property(e => e.TerminatedAt).HasColumnName("terminated_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedById).HasColumnName("updated_by_id").HasColumnType("int(11)");

            // One row per supplier: two rows for the same supplier would split its concentration in two (S51 D5). The
            // collation is case-insensitive, so "AWS" and "aws" collide.
            entity.HasIndex(e => e.Name, "uq_third_parties_name").IsUnique();
            entity.HasIndex(e => e.EntityId, "idx_third_parties_entity_id");
            entity.HasIndex(e => e.OwnerId, "idx_third_parties_owner_id");
            entity.HasIndex(e => e.Status, "idx_third_parties_status");
            entity.HasIndex(e => e.CreatedById, "idx_third_parties_created_by_id");
            entity.HasIndex(e => e.UpdatedById, "idx_third_parties_updated_by_id");

            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_third_parties_entity_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Owner)
                .WithMany()
                .HasForeignKey(e => e.OwnerId)
                .HasConstraintName("fk_third_parties_owner_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_third_parties_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.UpdatedBy)
                .WithMany()
                .HasForeignKey(e => e.UpdatedById)
                .HasConstraintName("fk_third_parties_updated_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ThirdPartyLink>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("third_party_links", t => t.HasCheckConstraint("ck_third_party_links_kind",
                    "`kind` >= 1 AND `kind` <= 3"))
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.ThirdPartyId).HasColumnName("third_party_id").HasColumnType("int(11)");
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.Kind).HasColumnName("kind").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.Description).HasColumnName("description").HasColumnType("varchar(500)")
                .HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");

            entity.HasIndex(e => new { e.ThirdPartyId, e.EntityId }, "uq_third_party_links_third_party_id_entity_id")
                .IsUnique();
            entity.HasIndex(e => e.EntityId, "idx_third_party_links_entity_id");
            entity.HasIndex(e => e.CreatedById, "idx_third_party_links_created_by_id");

            // RESTRICT: a supplier that supplies something is in use (S51 §4.9).
            entity.HasOne(e => e.ThirdParty)
                .WithMany(t => t.Links)
                .HasForeignKey(e => e.ThirdPartyId)
                .HasConstraintName("fk_third_party_links_third_party_id")
                .OnDelete(DeleteBehavior.Restrict);

            // CASCADE: deleting the service, process or data record removes what pointed at it, as a BIA dependency does.
            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_third_party_links_entity_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_third_party_links_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureThirdPartyDeclarations(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ThirdPartySubprocessor>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("third_party_subprocessors")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.ThirdPartyId).HasColumnName("third_party_id").HasColumnType("int(11)");
            entity.Property(e => e.Name).HasColumnName("name").HasColumnType("varchar(200)").HasMaxLength(200);
            entity.Property(e => e.SubprocessorThirdPartyId).HasColumnName("subprocessor_third_party_id")
                .HasColumnType("int(11)");
            entity.Property(e => e.Service).HasColumnName("service").HasColumnType("varchar(500)").HasMaxLength(500);
            entity.Property(e => e.Country).HasColumnName("country").HasColumnType("varchar(2)").HasMaxLength(2);
            entity.Property(e => e.ProcessesPersonalData).HasColumnName("processes_personal_data")
                .HasColumnType("tinyint(1)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");

            entity.HasIndex(e => new { e.ThirdPartyId, e.Name }, "uq_third_party_subprocessors_third_party_id_name")
                .IsUnique();
            entity.HasIndex(e => e.SubprocessorThirdPartyId, "idx_third_party_subprocessors_subprocessor_third_party_id");
            entity.HasIndex(e => e.CreatedById, "idx_third_party_subprocessors_created_by_id");

            entity.HasOne(e => e.ThirdParty)
                .WithMany(t => t.Subprocessors)
                .HasForeignKey(e => e.ThirdPartyId)
                .HasConstraintName("fk_third_party_subprocessors_third_party_id")
                .OnDelete(DeleteBehavior.Cascade);

            // RESTRICT: a supplier another supplier names as its sub-processor is in use (S51 §4.9). No CHECK refuses
            // naming oneself — the service does: a CHECK may not name a column a foreign key acts on.
            entity.HasOne(e => e.SubprocessorThirdParty)
                .WithMany()
                .HasForeignKey(e => e.SubprocessorThirdPartyId)
                .HasConstraintName("fk_third_party_subprocessors_subprocessor_third_party_id")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_third_party_subprocessors_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ThirdPartyDataLocation>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("third_party_data_locations", t => t.HasCheckConstraint(
                    "ck_third_party_data_locations_purpose", "`purpose` >= 1 AND `purpose` <= 4"))
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.ThirdPartyId).HasColumnName("third_party_id").HasColumnType("int(11)");
            entity.Property(e => e.Country).HasColumnName("country").HasColumnType("varchar(2)").HasMaxLength(2);
            entity.Property(e => e.Region).HasColumnName("region").HasColumnType("varchar(200)").HasMaxLength(200);
            entity.Property(e => e.Purpose).HasColumnName("purpose").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");

            entity.HasIndex(e => new { e.ThirdPartyId, e.Country, e.Purpose },
                "uq_third_party_data_locations_third_party_id_country_purpose").IsUnique();
            entity.HasIndex(e => e.CreatedById, "idx_third_party_data_locations_created_by_id");

            entity.HasOne(e => e.ThirdParty)
                .WithMany(t => t.DataLocations)
                .HasForeignKey(e => e.ThirdPartyId)
                .HasConstraintName("fk_third_party_data_locations_third_party_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_third_party_data_locations_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureThirdPartyAssessments(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ThirdPartyAssessment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("third_party_assessments", t =>
                {
                    t.HasCheckConstraint("ck_third_party_assessments_variant", "`variant` >= 1 AND `variant` <= 4");
                    t.HasCheckConstraint("ck_third_party_assessments_expected_question_count",
                        "`expected_question_count` >= 1 AND `expected_question_count` <= 2000");
                    t.HasCheckConstraint("ck_third_party_assessments_voided",
                        "(`voided_at` IS NULL AND `void_reason` IS NULL) OR (`voided_at` IS NOT NULL AND `void_reason` IS NOT NULL)");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.ThirdPartyId).HasColumnName("third_party_id").HasColumnType("int(11)");
            entity.Property(e => e.Variant).HasColumnName("variant").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.FrameworkVersion).HasColumnName("framework_version").HasColumnType("varchar(20)")
                .HasMaxLength(20);
            entity.Property(e => e.ExpectedQuestionCount).HasColumnName("expected_question_count")
                .HasColumnType("int(11)");
            entity.Property(e => e.RespondedAt).HasColumnName("responded_at").HasColumnType("datetime");
            entity.Property(e => e.ValidUntil).HasColumnName("valid_until").HasColumnType("datetime");
            entity.Property(e => e.EvidenceReference).HasColumnName("evidence_reference").HasColumnType("varchar(500)")
                .HasMaxLength(500);
            entity.Property(e => e.Notes).HasColumnName("notes").HasColumnType("varchar(2000)").HasMaxLength(2000);
            entity.Property(e => e.AnswersUpdatedAt).HasColumnName("answers_updated_at").HasColumnType("datetime");
            entity.Property(e => e.AnswersUpdatedById).HasColumnName("answers_updated_by_id").HasColumnType("int(11)");
            entity.Property(e => e.VoidedAt).HasColumnName("voided_at").HasColumnType("datetime");
            entity.Property(e => e.VoidedById).HasColumnName("voided_by_id").HasColumnType("int(11)");
            entity.Property(e => e.VoidReason).HasColumnName("void_reason").HasColumnType("varchar(1000)")
                .HasMaxLength(1000);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");

            entity.HasIndex(e => e.ThirdPartyId, "idx_third_party_assessments_third_party_id");
            entity.HasIndex(e => e.CreatedById, "idx_third_party_assessments_created_by_id");
            entity.HasIndex(e => e.AnswersUpdatedById, "idx_third_party_assessments_answers_updated_by_id");
            entity.HasIndex(e => e.VoidedById, "idx_third_party_assessments_voided_by_id");

            // RESTRICT: an assessment is evidence about the supplier, and a supplier with evidence is in use.
            entity.HasOne(e => e.ThirdParty)
                .WithMany()
                .HasForeignKey(e => e.ThirdPartyId)
                .HasConstraintName("fk_third_party_assessments_third_party_id")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_third_party_assessments_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.AnswersUpdatedBy)
                .WithMany()
                .HasForeignKey(e => e.AnswersUpdatedById)
                .HasConstraintName("fk_third_party_assessments_answers_updated_by_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.VoidedBy)
                .WithMany()
                .HasForeignKey(e => e.VoidedById)
                .HasConstraintName("fk_third_party_assessments_voided_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ThirdPartyAssessmentAnswer>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("third_party_assessment_answers", t =>
                {
                    t.HasCheckConstraint("ck_third_party_assessment_answers_answer", "`answer` >= 1 AND `answer` <= 4");
                    t.HasCheckConstraint("ck_third_party_assessment_answers_preferred_answer",
                        "`preferred_answer` IS NULL OR (`preferred_answer` >= 1 AND `preferred_answer` <= 2)");
                    t.HasCheckConstraint("ck_third_party_assessment_answers_weight", "`weight` >= 1 AND `weight` <= 100");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.AssessmentId).HasColumnName("assessment_id").HasColumnType("int(11)");
            entity.Property(e => e.QuestionId).HasColumnName("question_id").HasColumnType("varchar(20)")
                .HasMaxLength(20);
            entity.Property(e => e.Answer).HasColumnName("answer").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.PreferredAnswer).HasColumnName("preferred_answer").HasColumnType("int(11)")
                .HasConversion<int?>();
            entity.Property(e => e.Weight).HasColumnName("weight").HasColumnType("int(11)");
            entity.Property(e => e.Critical).HasColumnName("critical").HasColumnType("tinyint(1)");
            entity.Property(e => e.Notes).HasColumnName("notes").HasColumnType("varchar(2000)").HasMaxLength(2000);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");

            entity.HasIndex(e => new { e.AssessmentId, e.QuestionId },
                "uq_third_party_assessment_answers_assessment_id_question_id").IsUnique();

            entity.HasOne(e => e.Assessment)
                .WithMany(a => a.Answers)
                .HasForeignKey(e => e.AssessmentId)
                .HasConstraintName("fk_third_party_assessment_answers_assessment_id")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureThirdPartySboms(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ThirdPartySbom>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("third_party_sboms", t =>
                {
                    t.HasCheckConstraint("ck_third_party_sboms_format", "`format` >= 1 AND `format` <= 2");
                    t.HasCheckConstraint("ck_third_party_sboms_counts",
                        "`document_size_bytes` >= 0 AND `component_count` >= 0");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.ThirdPartyId).HasColumnName("third_party_id").HasColumnType("int(11)");
            entity.Property(e => e.ComponentName).HasColumnName("component_name").HasColumnType("varchar(200)")
                .HasMaxLength(200);
            entity.Property(e => e.ComponentVersion).HasColumnName("component_version").HasColumnType("varchar(100)")
                .HasMaxLength(100);
            entity.Property(e => e.Format).HasColumnName("format").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.SpecVersion).HasColumnName("spec_version").HasColumnType("varchar(20)")
                .HasMaxLength(20);
            entity.Property(e => e.SerialNumber).HasColumnName("serial_number").HasColumnType("varchar(300)")
                .HasMaxLength(300);
            entity.Property(e => e.DocumentSha256).HasColumnName("document_sha256").HasColumnType("varchar(64)")
                .HasMaxLength(64);
            entity.Property(e => e.DocumentSizeBytes).HasColumnName("document_size_bytes").HasColumnType("int(11)");
            entity.Property(e => e.FileName).HasColumnName("file_name").HasColumnType("varchar(255)").HasMaxLength(255);
            entity.Property(e => e.ComponentCount).HasColumnName("component_count").HasColumnType("int(11)");
            entity.Property(e => e.UploadedAt).HasColumnName("uploaded_at").HasColumnType("datetime");
            entity.Property(e => e.UploadedById).HasColumnName("uploaded_by_id").HasColumnType("int(11)");

            // The same document twice for the same supplier is one SBOM.
            entity.HasIndex(e => new { e.ThirdPartyId, e.DocumentSha256 }, "uq_third_party_sboms_third_party_id_document_sha256")
                .IsUnique();
            entity.HasIndex(e => e.UploadedById, "idx_third_party_sboms_uploaded_by_id");

            entity.HasOne(e => e.ThirdParty)
                .WithMany()
                .HasForeignKey(e => e.ThirdPartyId)
                .HasConstraintName("fk_third_party_sboms_third_party_id")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.UploadedBy)
                .WithMany()
                .HasForeignKey(e => e.UploadedById)
                .HasConstraintName("fk_third_party_sboms_uploaded_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ThirdPartySbomComponent>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("third_party_sbom_components")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.SbomId).HasColumnName("sbom_id").HasColumnType("int(11)");
            entity.Property(e => e.Name).HasColumnName("name").HasColumnType("varchar(300)").HasMaxLength(300);
            entity.Property(e => e.Version).HasColumnName("version").HasColumnType("varchar(200)").HasMaxLength(200);
            entity.Property(e => e.Purl).HasColumnName("purl").HasColumnType("varchar(1000)").HasMaxLength(1000);
            entity.Property(e => e.License).HasColumnName("license").HasColumnType("varchar(500)").HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");

            entity.HasIndex(e => e.SbomId, "idx_third_party_sbom_components_sbom_id");
            entity.HasIndex(e => e.Name, "idx_third_party_sbom_components_name");

            entity.HasOne(e => e.Sbom)
                .WithMany(s => s.Components)
                .HasForeignKey(e => e.SbomId)
                .HasConstraintName("fk_third_party_sbom_components_sbom_id")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
