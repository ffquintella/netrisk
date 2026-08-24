using System;
using API.Tests.DI;
using Microsoft.Extensions.DependencyInjection;

namespace API.Tests.APITests;

public class BaseControllerTest
{
    protected readonly IServiceProvider _serviceProvider = ServiceRegistration.GetServiceProvider();

    /// <summary>
    /// Builds a controller over the shared mocks, with <paramref name="configure"/> layered on top
    /// so a test class can supply doubles only it cares about.
    /// </summary>
    protected static TController ResolveController<TController>(Action<IServiceCollection> configure)
        where TController : notnull
    {
        return ServiceRegistration.GetServiceProvider(configure).GetRequiredService<TController>();
    }
}
