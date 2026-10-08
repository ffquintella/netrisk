using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// Stage 9.11 — the LGPD data catalogue (S52 §4): <c>legal_requirements</c> (laws, regulations, contracts and internal
/// norms), <c>data_catalogue_entries</c> with <c>data_catalogue_purposes</c> and <c>data_catalogue_locations</c> (the
/// catalogue of an <c>organizationData</c> node: personal-data category, purposes with their legal basis, retention,
/// location and international transfer), <c>dpias</c> and <c>dpia_links</c> (the RIPD and what it covers), and
/// <c>risk_legal_requirements</c> (the requirements of a risk, as links instead of free text).
///
/// Named per the Track 6 convention: snake_case columns via <c>HasColumnName</c>, <c>fk_</c>/<c>idx_</c>/<c>uq_</c>/
/// <c>ck_</c> prefixes, UTC <c>datetime</c>, <c>int</c> + <c>HasConversion</c> for the enums, <c>tinyint(1)</c> for the
/// booleans, <c>varchar(n)</c> and never <c>char(n)</c> — the country codes are <c>varchar(2)</c> —, <c>text</c> for the
/// RIPD summary. Every CHECK is declared on the model too, so the snapshot carries the same DDL as
/// <c>Structure/100.sql</c>, and no CHECK names a column a <c>SET NULL</c> foreign key may clear.
///
/// <b>A requirement in use cannot be deleted</b> (S52 §4.9): the purposes, the retentions and the risk links reference it
/// with <c>RESTRICT</c>. The catalogue goes with its node (<c>CASCADE</c>); the trail stays. No inverse navigation on
/// <see cref="Entity"/>, <see cref="User"/>, <see cref="Risk"/> or <see cref="ThirdParty"/>, so no existing payload
/// changes. The query filter of the risk links is in <c>NRDbContext.EntityScope.cs</c>; the rest is the organization's,
/// like the entity map it describes (S52 D10).
/// </summary>
public partial class NRDbContext
{
    public virtual DbSet<LegalRequirement> LegalRequirements { get; set; } = null!;

    public virtual DbSet<DataCatalogueEntry> DataCatalogueEntries { get; set; } = null!;

    public virtual DbSet<DataCataloguePurpose> DataCataloguePurposes { get; set; } = null!;

    public virtual DbSet<DataCatalogueLocation> DataCatalogueLocations { get; set; } = null!;

    public virtual DbSet<Dpia> Dpias { get; set; } = null!;

    public virtual DbSet<DpiaLink> DpiaLinks { get; set; } = null!;

    public virtual DbSet<RiskLegalRequirement> RiskLegalRequirements { get; set; } = null!;

    private static void ConfigureDataCatalogue(ModelBuilder modelBuilder)
    {
        ConfigureLegalRequirements(modelBuilder);
        ConfigureDataCatalogueEntries(modelBuilder);
        ConfigureDpias(modelBuilder);
    }

    private static void ConfigureLegalRequirements(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LegalRequirement>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("legal_requirements", t => t.HasCheckConstraint("ck_legal_requirements_kind",
                    "`kind` >= 1 AND `kind` <= 4"))
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.Code).HasColumnName("code").HasColumnType("varchar(100)").HasMaxLength(100);
            entity.Property(e => e.Title).HasColumnName("title").HasColumnType("varchar(300)").HasMaxLength(300);
            entity.Property(e => e.Kind).HasColumnName("kind").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.Description).HasColumnName("description").HasColumnType("varchar(2000)")
                .HasMaxLength(2000);
            entity.Property(e => e.Reference).HasColumnName("reference").HasColumnType("varchar(500)").HasMaxLength(500);
            entity.Property(e => e.ThirdPartyId).HasColumnName("third_party_id").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedById).HasColumnName("updated_by_id").HasColumnType("int(11)");

            // One row per citation; the collation is case-insensitive, so "LGPD art. 46" and "lgpd ART. 46" collide.
            entity.HasIndex(e => e.Code, "uq_legal_requirements_code").IsUnique();
            entity.HasIndex(e => e.ThirdPartyId, "idx_legal_requirements_third_party_id");
            entity.HasIndex(e => e.CreatedById, "idx_legal_requirements_created_by_id");
            entity.HasIndex(e => e.UpdatedById, "idx_legal_requirements_updated_by_id");

            // RESTRICT: a third party named as a contract's counterparty is in use (S52 §4.9, ThirdPartyReferences).
            entity.HasOne(e => e.ThirdParty)
                .WithMany()
                .HasForeignKey(e => e.ThirdPartyId)
                .HasConstraintName("fk_legal_requirements_third_party_id")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_legal_requirements_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.UpdatedBy)
                .WithMany()
                .HasForeignKey(e => e.UpdatedById)
                .HasConstraintName("fk_legal_requirements_updated_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RiskLegalRequirement>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("risk_legal_requirements")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.RiskId).HasColumnName("risk_id").HasColumnType("int(11)");
            entity.Property(e => e.LegalRequirementId).HasColumnName("legal_requirement_id").HasColumnType("int(11)");
            entity.Property(e => e.Note).HasColumnName("note").HasColumnType("varchar(500)").HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");

            entity.HasIndex(e => new { e.RiskId, e.LegalRequirementId },
                "uq_risk_legal_requirements_risk_id_legal_requirement_id").IsUnique();
            entity.HasIndex(e => e.LegalRequirementId, "idx_risk_legal_requirements_legal_requirement_id");
            entity.HasIndex(e => e.CreatedById, "idx_risk_legal_requirements_created_by_id");

            entity.HasOne(e => e.Risk)
                .WithMany()
                .HasForeignKey(e => e.RiskId)
                .HasConstraintName("fk_risk_legal_requirements_risk_id")
                .OnDelete(DeleteBehavior.Cascade);

            // RESTRICT: a requirement a risk cites is in use.
            entity.HasOne(e => e.LegalRequirement)
                .WithMany()
                .HasForeignKey(e => e.LegalRequirementId)
                .HasConstraintName("fk_risk_legal_requirements_legal_requirement_id")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_risk_legal_requirements_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureDataCatalogueEntries(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DataCatalogueEntry>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("data_catalogue_entries", t =>
                {
                    t.HasCheckConstraint("ck_data_catalogue_entries_personal_data",
                        "`personal_data` IS NULL OR (`personal_data` >= 1 AND `personal_data` <= 4)");
                    t.HasCheckConstraint("ck_data_catalogue_entries_transfer_mechanism",
                        "`transfer_mechanism` IS NULL OR (`transfer_mechanism` >= 1 AND `transfer_mechanism` <= 12)");
                    t.HasCheckConstraint("ck_data_catalogue_entries_retention_period",
                        "`retention_period_months` IS NULL OR (`retention_period_months` >= 0 AND `retention_period_months` <= 1200)");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.PersonalData).HasColumnName("personal_data").HasColumnType("int(11)")
                .HasConversion<int?>();
            entity.Property(e => e.InvolvesMinors).HasColumnName("involves_minors").HasColumnType("tinyint(1)");
            entity.Property(e => e.LargeVolume).HasColumnName("large_volume").HasColumnType("tinyint(1)");
            entity.Property(e => e.StrategicResearch).HasColumnName("strategic_research").HasColumnType("tinyint(1)");
            entity.Property(e => e.DataSubjects).HasColumnName("data_subjects").HasColumnType("varchar(500)")
                .HasMaxLength(500);
            entity.Property(e => e.DataCategories).HasColumnName("data_categories").HasColumnType("varchar(1000)")
                .HasMaxLength(1000);
            entity.Property(e => e.RetentionPeriodMonths).HasColumnName("retention_period_months")
                .HasColumnType("int(11)");
            entity.Property(e => e.RetentionTrigger).HasColumnName("retention_trigger").HasColumnType("varchar(500)")
                .HasMaxLength(500);
            entity.Property(e => e.RetentionBasis).HasColumnName("retention_basis").HasColumnType("varchar(1000)")
                .HasMaxLength(1000);
            entity.Property(e => e.RetentionRequirementId).HasColumnName("retention_requirement_id")
                .HasColumnType("int(11)");
            entity.Property(e => e.RetentionReviewDueAt).HasColumnName("retention_review_due_at")
                .HasColumnType("datetime");
            entity.Property(e => e.RetentionReviewedAt).HasColumnName("retention_reviewed_at").HasColumnType("datetime");
            entity.Property(e => e.InternationalTransfer).HasColumnName("international_transfer")
                .HasColumnType("tinyint(1)");
            entity.Property(e => e.TransferMechanism).HasColumnName("transfer_mechanism").HasColumnType("int(11)")
                .HasConversion<int?>();
            entity.Property(e => e.Notes).HasColumnName("notes").HasColumnType("varchar(2000)").HasMaxLength(2000);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedById).HasColumnName("updated_by_id").HasColumnType("int(11)");

            // One catalogue per data record (S52 §4.2, S51 D4).
            entity.HasIndex(e => e.EntityId, "uq_data_catalogue_entries_entity_id").IsUnique();
            entity.HasIndex(e => e.RetentionRequirementId, "idx_data_catalogue_entries_retention_requirement_id");
            entity.HasIndex(e => e.CreatedById, "idx_data_catalogue_entries_created_by_id");
            entity.HasIndex(e => e.UpdatedById, "idx_data_catalogue_entries_updated_by_id");

            // CASCADE: the catalogue describes the node and goes with it; the trail stays (S52 R5).
            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_data_catalogue_entries_entity_id")
                .OnDelete(DeleteBehavior.Cascade);

            // RESTRICT: a requirement a retention cites is in use.
            entity.HasOne(e => e.RetentionRequirement)
                .WithMany()
                .HasForeignKey(e => e.RetentionRequirementId)
                .HasConstraintName("fk_data_catalogue_entries_retention_requirement_id")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_data_catalogue_entries_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.UpdatedBy)
                .WithMany()
                .HasForeignKey(e => e.UpdatedById)
                .HasConstraintName("fk_data_catalogue_entries_updated_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DataCataloguePurpose>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("data_catalogue_purposes", t => t.HasCheckConstraint("ck_data_catalogue_purposes_legal_basis",
                    "`legal_basis` IS NULL OR (`legal_basis` >= 1 AND `legal_basis` <= 18)"))
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.EntryId).HasColumnName("entry_id").HasColumnType("int(11)");
            entity.Property(e => e.Purpose).HasColumnName("purpose").HasColumnType("varchar(300)").HasMaxLength(300);
            entity.Property(e => e.LegalBasis).HasColumnName("legal_basis").HasColumnType("int(11)")
                .HasConversion<int?>();
            entity.Property(e => e.LegalRequirementId).HasColumnName("legal_requirement_id").HasColumnType("int(11)");
            entity.Property(e => e.BasisReference).HasColumnName("basis_reference").HasColumnType("varchar(500)")
                .HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");

            entity.HasIndex(e => new { e.EntryId, e.Purpose }, "uq_data_catalogue_purposes_entry_id_purpose").IsUnique();
            entity.HasIndex(e => e.LegalRequirementId, "idx_data_catalogue_purposes_legal_requirement_id");
            entity.HasIndex(e => e.CreatedById, "idx_data_catalogue_purposes_created_by_id");

            entity.HasOne(e => e.Entry)
                .WithMany(x => x.Purposes)
                .HasForeignKey(e => e.EntryId)
                .HasConstraintName("fk_data_catalogue_purposes_entry_id")
                .OnDelete(DeleteBehavior.Cascade);

            // RESTRICT: a requirement a purpose rests on is in use.
            entity.HasOne(e => e.LegalRequirement)
                .WithMany()
                .HasForeignKey(e => e.LegalRequirementId)
                .HasConstraintName("fk_data_catalogue_purposes_legal_requirement_id")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_data_catalogue_purposes_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DataCatalogueLocation>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("data_catalogue_locations", t => t.HasCheckConstraint(
                    "ck_data_catalogue_locations_purpose", "`purpose` >= 1 AND `purpose` <= 4"))
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.EntryId).HasColumnName("entry_id").HasColumnType("int(11)");
            entity.Property(e => e.Country).HasColumnName("country").HasColumnType("varchar(2)").HasMaxLength(2);
            entity.Property(e => e.Region).HasColumnName("region").HasColumnType("varchar(200)").HasMaxLength(200);
            entity.Property(e => e.Purpose).HasColumnName("purpose").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");

            entity.HasIndex(e => new { e.EntryId, e.Country, e.Purpose },
                "uq_data_catalogue_locations_entry_id_country_purpose").IsUnique();
            entity.HasIndex(e => e.CreatedById, "idx_data_catalogue_locations_created_by_id");

            entity.HasOne(e => e.Entry)
                .WithMany(x => x.Locations)
                .HasForeignKey(e => e.EntryId)
                .HasConstraintName("fk_data_catalogue_locations_entry_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_data_catalogue_locations_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureDpias(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Dpia>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("dpias", t =>
                {
                    t.HasCheckConstraint("ck_dpias_status", "`status` >= 1 AND `status` <= 3");
                    t.HasCheckConstraint("ck_dpias_residual_risk",
                        "`residual_risk` IS NULL OR (`residual_risk` >= 1 AND `residual_risk` <= 3)");
                    t.HasCheckConstraint("ck_dpias_approved", "`status` <> 2 OR `approved_at` IS NOT NULL");
                    t.HasCheckConstraint("ck_dpias_retired",
                        "(`status` = 3 AND `retired_at` IS NOT NULL AND `retire_reason` IS NOT NULL) OR (`status` <> 3 AND `retired_at` IS NULL)");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.Title).HasColumnName("title").HasColumnType("varchar(200)").HasMaxLength(200);
            entity.Property(e => e.Status).HasColumnName("status").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.Summary).HasColumnName("summary").HasColumnType("text");
            entity.Property(e => e.DocumentReference).HasColumnName("document_reference").HasColumnType("varchar(500)")
                .HasMaxLength(500);
            entity.Property(e => e.ResidualRisk).HasColumnName("residual_risk").HasColumnType("int(11)")
                .HasConversion<int?>();
            entity.Property(e => e.PerformedAt).HasColumnName("performed_at").HasColumnType("datetime");
            entity.Property(e => e.NextReviewDueAt).HasColumnName("next_review_due_at").HasColumnType("datetime");
            entity.Property(e => e.ApprovedAt).HasColumnName("approved_at").HasColumnType("datetime");
            entity.Property(e => e.ApprovedById).HasColumnName("approved_by_id").HasColumnType("int(11)");
            entity.Property(e => e.RetiredAt).HasColumnName("retired_at").HasColumnType("datetime");
            entity.Property(e => e.RetiredById).HasColumnName("retired_by_id").HasColumnType("int(11)");
            entity.Property(e => e.RetireReason).HasColumnName("retire_reason").HasColumnType("varchar(1000)")
                .HasMaxLength(1000);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedById).HasColumnName("updated_by_id").HasColumnType("int(11)");

            entity.HasIndex(e => e.Status, "idx_dpias_status");
            entity.HasIndex(e => e.ApprovedById, "idx_dpias_approved_by_id");
            entity.HasIndex(e => e.RetiredById, "idx_dpias_retired_by_id");
            entity.HasIndex(e => e.CreatedById, "idx_dpias_created_by_id");
            entity.HasIndex(e => e.UpdatedById, "idx_dpias_updated_by_id");

            entity.HasOne(e => e.ApprovedBy)
                .WithMany()
                .HasForeignKey(e => e.ApprovedById)
                .HasConstraintName("fk_dpias_approved_by_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.RetiredBy)
                .WithMany()
                .HasForeignKey(e => e.RetiredById)
                .HasConstraintName("fk_dpias_retired_by_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_dpias_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.UpdatedBy)
                .WithMany()
                .HasForeignKey(e => e.UpdatedById)
                .HasConstraintName("fk_dpias_updated_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DpiaLink>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("dpia_links", t => t.HasCheckConstraint("ck_dpia_links_kind", "`kind` >= 1 AND `kind` <= 2"))
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.DpiaId).HasColumnName("dpia_id").HasColumnType("int(11)");
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.Kind).HasColumnName("kind").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");

            entity.HasIndex(e => new { e.DpiaId, e.EntityId }, "uq_dpia_links_dpia_id_entity_id").IsUnique();
            entity.HasIndex(e => e.EntityId, "idx_dpia_links_entity_id");
            entity.HasIndex(e => e.CreatedById, "idx_dpia_links_created_by_id");

            entity.HasOne(e => e.Dpia)
                .WithMany(d => d.Links)
                .HasForeignKey(e => e.DpiaId)
                .HasConstraintName("fk_dpia_links_dpia_id")
                .OnDelete(DeleteBehavior.Cascade);

            // CASCADE: deleting the data record or the process removes what pointed at it, as a third-party link does.
            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_dpia_links_entity_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_dpia_links_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
