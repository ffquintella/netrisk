using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// The external secret-vault schema: connections to a vault, serviced by a secret-vault plugin.
///
/// One table, deliberately. The references that point through it live in the credential columns that
/// already exist across the product rather than in a join table — see
/// <see cref="Model.Secrets.SecretReference"/> for why, and for what that costs.
///
/// Named per the Track 6 convention: snake_case columns via <c>HasColumnName</c>, <c>fk_</c>/
/// <c>idx_</c>/<c>uq_</c> prefixes, <c>tinyint(1)</c> booleans, UTC <c>datetime</c>, and
/// <c>varchar(n)</c>/<c>text</c> for strings — never <c>char(n)</c>, which EF Core 10 reads as a
/// primitive collection of char and dies on.
/// </summary>
public partial class NRDbContext
{
    public virtual DbSet<SecretVaultConnection> SecretVaultConnections { get; set; } = null!;

    private static void ConfigureSecretVaults(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SecretVaultConnection>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("secret_vault_connections")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255);
            entity.Property(e => e.PluginName).HasColumnName("plugin_name").HasMaxLength(255);
            entity.Property(e => e.BaseUrl).HasColumnName("base_url").HasMaxLength(512);
            entity.Property(e => e.EncryptedApiKey).HasColumnName("encrypted_api_key").HasColumnType("text");
            entity.Property(e => e.MachineId).HasColumnName("machine_id").HasMaxLength(255);
            entity.Property(e => e.Enabled).HasColumnName("enabled").HasColumnType("tinyint(1)");
            entity.Property(e => e.CacheTtlMinutes).HasColumnName("cache_ttl_minutes").HasColumnType("int(11)");
            entity.Property(e => e.LastTestAt).HasColumnName("last_test_at").HasColumnType("datetime");
            entity.Property(e => e.LastTestSucceeded).HasColumnName("last_test_succeeded")
                .HasColumnType("tinyint(1)");
            entity.Property(e => e.LastTestMessage).HasColumnName("last_test_message").HasColumnType("text");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");

            entity.HasIndex(e => e.Name, "uq_secret_vault_connections_name").IsUnique();
            entity.HasIndex(e => e.CreatedById, "idx_secret_vault_connections_created_by_id");

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_secret_vault_connections_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
