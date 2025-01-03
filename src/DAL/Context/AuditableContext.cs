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