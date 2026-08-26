using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// The schema the security findings Track 7 deferred to Track 8 needed: per-<c>jti</c> token
/// revocation (NR-2026-028), a persisted brute-force counter shared across API instances
/// (NR-2026-008b), and an <c>entity_id</c> on attachments so they come under the Track 2.3 query
/// filters (NR-2026-017).
///
/// Its own partial and its own migration, so the security fix is a phase an operator can identify
/// and apply on its own schedule rather than something buried inside a governance feature.
/// </summary>
public partial class NRDbContext
{
    public virtual DbSet<RevokedToken> RevokedTokens { get; set; } = null!;

    public virtual DbSet<LoginAttempt> LoginAttempts { get; set; } = null!;

    private static void ConfigureDeferredSecuritySchema(ModelBuilder modelBuilder)
    {
        ConfigureFileEntityScope(modelBuilder);
        ConfigureTokenAndLoginState(modelBuilder);
    }

    /// <summary>
    /// Attachments finally carry an entity of their own (security finding NR-2026-017), which is
    /// what closes the cross-tenant read. A file with no entity stays visible: legacy rows predate
    /// the column, and hiding every existing attachment from every scoped user would be a
    /// data-loss-shaped regression rather than a fix.
    /// </summary>
    private void ApplyDeferredSecurityQueryFilters(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NrFile>().HasQueryFilter(e =>
            ScopeIsUnrestricted || e.EntityId == null || ScopeEntityIds.Contains(e.EntityId.Value));
    }

    /// <summary>Security finding NR-2026-017 — attachments join the entity-scoping model.</summary>
    private static void ConfigureFileEntityScope(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NrFile>(entity =>
        {
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");

            entity.HasIndex(e => e.EntityId, "idx_nr_files_entity_id");

            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_nr_files_entity_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    /// <summary>Security findings NR-2026-028 and NR-2026-008b.</summary>
    private static void ConfigureTokenAndLoginState(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RevokedToken>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("revoked_tokens")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.Jti).HasColumnName("jti").HasMaxLength(64);
            entity.Property(e => e.UserId).HasColumnName("user_id").HasColumnType("int(11)");
            entity.Property(e => e.RevokedAt).HasColumnName("revoked_at").HasColumnType("datetime");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at").HasColumnType("datetime");
            entity.Property(e => e.Reason).HasColumnName("reason").HasMaxLength(255);

            entity.HasIndex(e => e.Jti, "uq_revoked_tokens_jti").IsUnique();
            entity.HasIndex(e => e.ExpiresAt, "idx_revoked_tokens_expires_at");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .HasConstraintName("fk_revoked_tokens_user_id")
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LoginAttempt>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("login_attempts")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.Identity).HasColumnName("identity").HasMaxLength(255);
            entity.Property(e => e.Source).HasColumnName("source").HasMaxLength(64);
            entity.Property(e => e.FailureCount).HasColumnName("failure_count").HasColumnType("int(11)");
            entity.Property(e => e.FirstFailureAt).HasColumnName("first_failure_at")
                .HasColumnType("datetime");
            entity.Property(e => e.LastFailureAt).HasColumnName("last_failure_at").HasColumnType("datetime");
            entity.Property(e => e.LockedUntil).HasColumnName("locked_until").HasColumnType("datetime");

            entity.HasIndex(e => new { e.Identity, e.Source }, "uq_login_attempts_identity_source")
                .IsUnique();
            entity.HasIndex(e => e.LastFailureAt, "idx_login_attempts_last_failure_at");
        });
    }
}
