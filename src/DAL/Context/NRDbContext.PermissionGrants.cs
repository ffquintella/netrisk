using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// The permission-grant record of S40 §6.4 and §11.4 (<see cref="PermissionGrantEvent"/>). Track 6 naming. The role and the
/// user are foreign keys with SET NULL so a role or user created in the same save gets its real id; the actor has none, so
/// the record outlives its author.
/// </summary>
public partial class NRDbContext
{
    public virtual DbSet<PermissionGrantEvent> PermissionGrantEvents { get; set; } = null!;

    private static void ConfigurePermissionGrants(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PermissionGrantEvent>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            // No CHECK names role_id or user_id: MariaDB refuses one on a column a SET NULL foreign key may clear.
            entity.ToTable("permission_grant_events", t =>
                {
                    t.HasCheckConstraint("ck_permission_grant_events_kind", "`kind` >= 1 AND `kind` <= 5");
                    t.HasCheckConstraint("ck_permission_grant_events_action", "`action` >= 1 AND `action` <= 2");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("bigint(20)");
            entity.Property(e => e.Kind).HasColumnName("kind").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.Action).HasColumnName("action").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.RoleId).HasColumnName("role_id").HasColumnType("int(11)");
            entity.Property(e => e.RoleName).HasColumnName("role_name").HasColumnType("varchar(100)").HasMaxLength(100);
            entity.Property(e => e.UserId).HasColumnName("user_id").HasColumnType("int(11)");
            entity.Property(e => e.PermissionKey).HasColumnName("permission_key").HasColumnType("varchar(100)")
                .HasMaxLength(100);
            entity.Property(e => e.OldValue).HasColumnName("old_value").HasColumnType("text");
            entity.Property(e => e.NewValue).HasColumnName("new_value").HasColumnType("text");
            entity.Property(e => e.ActorId).HasColumnName("actor_id").HasColumnType("int(11)");
            entity.Property(e => e.Actor).HasColumnName("actor").HasColumnType("varchar(64)").HasMaxLength(64);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");

            entity.HasIndex(e => new { e.RoleId, e.PermissionKey }, "idx_permission_grant_events_role_id_permission_key");
            entity.HasIndex(e => new { e.UserId, e.Kind }, "idx_permission_grant_events_user_id_kind");
            entity.HasIndex(e => new { e.Kind, e.RoleName }, "idx_permission_grant_events_kind_role_name");
            entity.HasIndex(e => e.ActorId, "idx_permission_grant_events_actor_id");

            entity.HasOne(e => e.Role)
                .WithMany()
                .HasForeignKey(e => e.RoleId)
                .HasConstraintName("fk_permission_grant_events_role_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .HasConstraintName("fk_permission_grant_events_user_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
