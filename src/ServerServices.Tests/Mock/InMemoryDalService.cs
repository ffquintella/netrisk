using System;
using DAL.Context;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ServerServices.Services;

namespace ServerServices.Tests.Mock;

/// <summary>
/// An <see cref="IDalService"/> backed by the EF Core in-memory provider. Every call to
/// <see cref="GetContext"/> returns a fresh <see cref="AuditableContext"/> bound to the same
/// in-memory database name, so the services-under-test can dispose contexts freely (most use
/// <c>using var context = dalService.GetContext()</c>) while the seeded data persists.
/// </summary>
public class InMemoryDalService : IDalService
{
    private readonly DbContextOptions<NRDbContext> _options;

    private int _saveChanges;

    /// <summary>How many <c>SaveChanges</c> have completed against this database, across all contexts.</summary>
    public int SaveChangesCount => Volatile.Read(ref _saveChanges);

    public string DatabaseName { get; }

    public InMemoryDalService(string databaseName)
    {
        DatabaseName = databaseName;
        _options = new DbContextOptionsBuilder<NRDbContext>()
            .UseInMemoryDatabase(databaseName)
            .EnableSensitiveDataLogging()
            // Counted, not printed. A service that writes a row per item instead of a batch is a
            // performance defect no assertion about the resulting data can see — the Vision One
            // inventory pass saved once per device and took 52 minutes on a real tenant — so the one
            // event that exposes it is made observable to the tests that care.
            .LogTo(_ => Interlocked.Increment(ref _saveChanges),
                new[] { CoreEventId.SaveChangesCompleted })
            .Options;
    }

    /// <summary>
    /// The scope handed to every context this service opens. Defaults to unrestricted so the
    /// existing service tests are unaffected; the entity-scoping tests set it to act as a user
    /// assigned to specific business entities.
    /// </summary>
    public EntityScope Scope { get; set; } = EntityScope.Unrestricted;

    public AuditableContext GetContext(bool withIdentity = true, bool bypassEntityScope = false) =>
        new(_options) { EntityScope = bypassEntityScope ? EntityScope.Unrestricted : Scope };

    public EntityScope GetCurrentEntityScope() => Scope;
}
