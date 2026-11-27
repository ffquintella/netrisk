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

    /// <summary>The name recorded in the Track 8 field-level trail when there is no user row to
    /// point at — a background job, the console client, a migration.</summary>
    public const string SystemActor = "system";

    /// <summary>
    /// Who is acting, for <c>audit_logs.actor</c> (Track 8 milestone 8.4.1). The DAL service sets it
    /// from the calling principal; anything that opens a context without one is the system.
    /// </summary>
    public string AuditActor { get; set; } = SystemActor;

    /// <summary>
    /// The <c>audit_logs.correlation_id</c> the governance trail stamps on the rows of this context's saves, instead of
    /// a fresh one per save. Set by a caller that must name the save in a record of its own — the risk-context batch's
    /// <c>batch_id</c> (S40 §8.5, T264). Null (the default): each save gets its own id. At most 64 characters are kept.
    /// </summary>
    public string? AuditCorrelationId { get; set; }

    /// <summary>
    /// Raised after a save that created, deleted or re-parented an <see cref="Entity"/> (T292). The DAL service
    /// points it at the host's entity-tree cache, because what an entity claim grants is that entity's subtree:
    /// a change of parent must move access on the next request, not after a new login.
    /// </summary>
    public Action? EntityTreeChanged { get; set; }

    /// <summary>
    /// Raised by every save that touches what an authentication handler puts in a principal: a user (other than the
    /// login timestamp alone), a role, a permission, a user's or a role's permission link, an entity assignment, or an
    /// entity removed (the database drops its assignments with it). The DAL service points it at the host's principal
    /// cache. Raised even when the save throws (dropping the cache is always safe), and again when the context is
    /// disposed if the save ran in an explicit transaction, so a principal read before the commit is not kept.
    /// <c>PrincipalCacheInvalidationTest</c> covers each kind; <c>PrincipalCacheIntegrationTests</c> the transaction.
    /// </summary>
    public Action? PrincipalInputsChanged { get; set; }

    private bool _principalChangeInTransaction;

    public override int SaveChanges()
    {
        // Ahead of BeforeSaveChanges, whose try/catch logs and swallows anything thrown inside it
        // so that an auditing failure cannot break a save. A scope violation must not be swallowed.
        ChangeTracker.DetectChanges();
        EnforceEntityScopeOnWrites();
        PermissionGrantRecorder.Record(this);
        var treeChanges = TouchesEntityTree();
        var principalChanges = TouchesPrincipalInputs();

        try
        {
            BeforeSaveChanges().ConfigureAwait(false).GetAwaiter().GetResult();
            var result = base.SaveChanges();
            if (treeChanges) EntityTreeChanged?.Invoke();
            return result;
        }
        finally
        {
            if (principalChanges) RaisePrincipalInputsChanged();
        }
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        EnforceEntityScopeOnWrites();

        // S40 §6.4, §11.4: who granted a watched permission, in this save and never swallowed — the approval's
        // sod_self_granted_approver reads it, and a grant that commits without its author would pass that check.
        PermissionGrantRecorder.Record(this);
        var treeChanges = TouchesEntityTree();
        var principalChanges = TouchesPrincipalInputs();

        try
        {
            await BeforeSaveChanges();
            var result = await base.SaveChangesAsync(cancellationToken);
            if (treeChanges) EntityTreeChanged?.Invoke();
            return result;
        }
        finally
        {
            if (principalChanges) RaisePrincipalInputsChanged();
        }
    }

    private void RaisePrincipalInputsChanged()
    {
        if (Database.CurrentTransaction != null) _principalChangeInTransaction = true;
        PrincipalInputsChanged?.Invoke();
    }

    public override void Dispose()
    {
        if (_principalChangeInTransaction)
        {
            _principalChangeInTransaction = false;
            PrincipalInputsChanged?.Invoke();
        }

        base.Dispose();
    }

    public override ValueTask DisposeAsync()
    {
        if (_principalChangeInTransaction)
        {
            _principalChangeInTransaction = false;
            PrincipalInputsChanged?.Invoke();
        }

        return base.DisposeAsync();
    }

    /// <summary>The login timestamp is written on every basic sign-in and feeds no claim, so it alone does not count.</summary>
    private static readonly HashSet<string> PrincipalNeutralUserProperties = new(StringComparer.Ordinal)
    {
        nameof(User.LastLogin)
    };

    /// <summary>Whether this save changes anything a principal is built from (see <see cref="PrincipalInputsChanged"/>).</summary>
    private bool TouchesPrincipalInputs() =>
        ChangeTracker.Entries().Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted &&
            e.Entity switch
            {
                User => e.State != EntityState.Modified ||
                        e.Properties.Any(p => p.IsModified && !PrincipalNeutralUserProperties.Contains(p.Metadata.Name)),
                Role or Permission or UserEntityRole => true,
                Entity => e.State == EntityState.Deleted,
                _ => e.Metadata.Name is PermissionGrantRecorder.RoleJoin or PermissionGrantRecorder.UserJoin
            });

    /// <summary>Whether this save adds or removes an entity, or changes one's parent.</summary>
    private bool TouchesEntityTree() =>
        ChangeTracker.Entries<Entity>().Any(e =>
            e.State is EntityState.Added or EntityState.Deleted ||
            (e.State == EntityState.Modified && e.Property(x => x.Parent).IsModified));
    

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

            // A caller who holds exactly one entity claim gets new records filed there automatically:
            // requiring them to state the only entity they were assigned would be busywork, and leaving it
            // null would create a row they could not then see. Since T292 that claim also covers its
            // descendants; the record still goes under the claim itself, the one entity the caller was given.
            if (entry.State == EntityState.Added && scoped.EntityId == null && EntityScope.ClaimedEntityIds.Count == 1)
            {
                scoped.EntityId = EntityScope.ClaimedEntityIds[0];
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
