using System.Security.Claims;
using DAL.Auditing;
using DAL.Entities;
using DAL.Exceptions;
using DAL.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

public class AuditableContext(DbContextOptions<NRDbContext> options) : NRDbContext(options)
{
    public int UserId { get; set; } = 0;
    public override int SaveChanges()
    {
        // Ahead of BeforeSaveChanges, whose try/catch logs and swallows anything thrown inside it
        // so that an auditing failure cannot break a save. A scope violation must not be swallowed.
        ChangeTracker.DetectChanges();
        EnforceEntityScopeOnWrites();

        BeforeSaveChanges().ConfigureAwait(false).GetAwaiter().GetResult();
        var result = base.SaveChanges();
        return result;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        EnforceEntityScopeOnWrites();

        await BeforeSaveChanges();
        var result = await base.SaveChangesAsync(cancellationToken);
        return result;
    }
    

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

    /// <summary>
    /// Refuses a write that would place a record outside the caller's business entities
    /// (Track 2 milestone 2.3.1).
    ///
    /// The query filters cover reads and, by extension, updates and deletes of rows the caller
    /// can see. They cannot cover the other direction: nothing stops a scoped caller from adding
    /// a row stamped with another entity's id, or from re-stamping one of their own rows on the
    /// way out. Doing this in <c>SaveChanges</c> rather than in each service is deliberate — it
    /// is the same reasoning as the query filter, and the reason the old per-service approach
    /// ended up enforced in exactly one place.
    /// </summary>
    private void EnforceEntityScopeOnWrites()
    {
        if (EntityScope.IsUnrestricted) return;

        foreach (var entry in ChangeTracker.Entries<IEntityScoped>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;

            var scoped = entry.Entity;

            // A caller who holds exactly one entity gets new records filed there automatically:
            // requiring them to state the only entity they have would be busywork, and leaving it
            // null would create a row they could not then see.
            if (entry.State == EntityState.Added && scoped.EntityId == null && EntityScope.EntityIds.Count == 1)
            {
                scoped.EntityId = EntityScope.EntityIds[0];
                continue;
            }

            if (!EntityScope.Allows(scoped.EntityId))
            {
                throw new EntityScopeViolationException(
                    entry.Entity.GetType().Name, scoped.EntityId, EntityScope.ToString());
            }
        }
    }
}
