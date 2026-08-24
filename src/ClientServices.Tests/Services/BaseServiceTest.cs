using System;
using ClientServices.Interfaces;
using ClientServices.Tests.DI;
using ClientServices.Tests.Mock;
using Microsoft.Extensions.DependencyInjection;

namespace ClientServices.Tests.Services;

public class BaseServiceTest
{
    protected readonly IServiceProvider _serviceProvider = ServiceRegistration.GetServiceProvider();

    /// <summary>
    /// Resolves a REST service wired to <paramref name="backend"/>, so the test controls every HTTP
    /// exchange it makes without touching a shared mock.
    /// </summary>
    protected static TService ResolveWith<TService>(StubRestBackend backend) where TService : notnull
    {
        return ServiceRegistration
            .GetServiceProvider(s => s.AddSingleton<IRestService>(backend))
            .GetRequiredService<TService>();
    }

    /// <summary>
    /// Resolves a REST service wired to <paramref name="backend"/> and to <paramref name="cache"/>,
    /// for the branches that answer from cache instead of going to HTTP. Services that take the
    /// cache as a constructor dependency get it from here; one that resolves it from
    /// <c>ServiceProviderAccessor</c> would pick up whichever provider another test class built last.
    /// </summary>
    protected static TService ResolveWith<TService>(StubRestBackend backend, IMemoryCacheService cache)
        where TService : notnull
    {
        return ServiceRegistration
            .GetServiceProvider(s =>
            {
                s.AddSingleton<IRestService>(backend);
                s.AddSingleton(cache);
            })
            .GetRequiredService<TService>();
    }
}
