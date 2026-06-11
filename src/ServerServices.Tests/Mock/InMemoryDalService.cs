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

    public AuditableContext GetContext(bool withIdentity = true) => new(_options);
}
