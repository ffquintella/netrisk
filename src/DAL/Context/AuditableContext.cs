using System.Security.Claims;
using DAL.Auditing;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

public class AuditableContext(DbContextOptions<NRDbContext> options) : NRDbContext(options)
{
    public int UserId { get; set; } = 0;
    public override int SaveChanges()
    {
        BeforeSaveChanges().ConfigureAwait(false).GetAwaiter().GetResult();
        var result = base.SaveChanges();
        return result;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await BeforeSaveChanges();
        var result = await base.SaveChangesAsync(cancellationToken);
        return result;
    }

    /*
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
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
            entity.Ignore(e => e.Password);
            
            entity.Property(e => e.RoleId)
                .HasColumnType("int(11)")
                .HasColumnName("role_id");
            entity.Property(e => e.Salt)
                .HasMaxLength(20)
                .HasColumnName("salt");
            entity.Ignore(e => e.Salt);
            
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
        });
    }
*/

    private async Task BeforeSaveChanges()
    {
        try
        {
            
            ChangeTracker.DetectChanges();

            var entries = ChangeTracker.Entries().ToList();
            
            foreach (var entry in entries)
            {
                if (entry.Entity is Base auditable)
                {
                    auditable.UpdateDate(entry.State);
                }

                if (entry.Entity is Entities.Audit || entry.State is EntityState.Detached or EntityState.Unchanged)
                    continue;

                var auditEntry = new AuditEntry(entry) { TableName = entry.Entity.GetType().Name, UserId = UserId };

                foreach (var property in entry.Properties)
                {
                    var propertyName = property.Metadata.Name;
                    if (property.Metadata.IsPrimaryKey())
                    {
                        if(property.CurrentValue is null)
                            continue;
                        auditEntry.KeyValues[propertyName] = property.CurrentValue;
                        continue;
                    }


                    switch (entry.State)
                    {
                        case EntityState.Added:
                            auditEntry.AuditType = AuditType.Create;
                            if(property.CurrentValue is null)
                                break;
                            auditEntry.NewValues[propertyName] = property.CurrentValue;

                            break;
                        case EntityState.Deleted:
                            auditEntry.AuditType = AuditType.Delete;
                            if(property.OriginalValue is null)
                                break;
                            auditEntry.OldValues[propertyName] = property.OriginalValue;

                            break;
                        case EntityState.Modified:
                            if (property.IsModified)
                            {
                                auditEntry.ChangedColumns.Add(propertyName);
                                auditEntry.AuditType = AuditType.Update;
                                if(property.OriginalValue is null || property.CurrentValue is null)
                                    break;
                                auditEntry.OldValues[propertyName] = property.OriginalValue;
                                auditEntry.NewValues[propertyName] = property.CurrentValue;

                            }
                            break;
                    }
                    
                }
                if(auditEntry.AuditType != AuditType.None) await Audits.AddAsync(auditEntry.ToAudit());
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error saving audit");
        }
    }

}