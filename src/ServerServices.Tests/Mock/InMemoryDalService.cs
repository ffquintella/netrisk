using System;
using DAL.Context;
using Microsoft.EntityFrameworkCore;
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
    public string DatabaseName { get; }

    public InMemoryDalService(string databaseName)
    {
        DatabaseName = databaseName;
        _options = new DbContextOptionsBuilder<NRDbContext>()
            .UseInMemoryDatabase(databaseName)
            .EnableSensitiveDataLogging()
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
