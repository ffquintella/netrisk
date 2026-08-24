using DAL.Context;
using Microsoft.EntityFrameworkCore;
using ServerServices.Services;

namespace API.Tests.Mock;

/// <summary>
/// An <see cref="IDalService"/> over the EF Core in-memory provider, for the controllers that read
/// the database directly instead of going through a domain service. Every <see cref="GetContext"/>
/// call returns a fresh context bound to the same database name, so controllers can dispose their
/// contexts while seeded data survives.
/// </summary>
/// <remarks>
/// Deliberately has no static <c>Create()</c> factory: it must not be picked up by the shared-mock
/// scan in <see cref="DI.ServiceRegistration"/>, because each test needs its own isolated database.
/// </remarks>
public class InMemoryDalService : IDalService
{
    private readonly DbContextOptions<NRDbContext> _options;

    public InMemoryDalService(string databaseName)
    {
        _options = new DbContextOptionsBuilder<NRDbContext>()
            .UseInMemoryDatabase(databaseName)
            .EnableSensitiveDataLogging()
            .Options;
    }

    public EntityScope Scope { get; set; } = EntityScope.Unrestricted;

    public AuditableContext GetContext(bool withIdentity = true, bool bypassEntityScope = false) =>
        new(_options) { EntityScope = bypassEntityScope ? EntityScope.Unrestricted : Scope };

    public EntityScope GetCurrentEntityScope() => Scope;
}
