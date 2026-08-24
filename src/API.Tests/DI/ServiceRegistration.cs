using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using API.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using ILogger = Serilog.ILogger;

namespace API.Tests.DI;

public static class ServiceRegistration
{
    /// <param name="configure">
    /// Runs after the shared mocks, so anything it registers wins on resolution. Use it to give a
    /// controller test its own per-test doubles instead of adding to the shared mocks in
    /// <c>API.Tests/Mock</c>, which every other test also sees.
    /// </param>
    public static IServiceProvider GetServiceProvider(Action<IServiceCollection>? configure = null)
    {
        var logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

        var factory = new SerilogLoggerFactory(logger);

        Log.Logger = logger;

        var services = new ServiceCollection();

        services.AddSingleton<ILoggerFactory>(factory);
        services.AddSingleton<ILogger>(new LoggerConfiguration().WriteTo.Console().CreateLogger());

        RegisterMocks(services);
        RegisterControllers(services);

        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Registers every <c>API.Tests.Mock.Mocked*.Create()</c> factory against the interface it
    /// returns, so covering a new controller means dropping in a mock file and nothing else.
    /// </summary>
    private static void RegisterMocks(IServiceCollection services)
    {
        foreach (var create in MockFactories())
            services.AddSingleton(create.ReturnType, _ => create.Invoke(null, null)!);
    }

    private static IEnumerable<MethodInfo> MockFactories()
    {
        return typeof(ServiceRegistration).Assembly
            .GetTypes()
            .Where(t => t.IsClass && t.Namespace == "API.Tests.Mock")
            .Select(t => t.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes))
            .Where(m => m != null && m.ReturnType.IsInterface)
            .Select(m => m!);
    }

    /// <summary>
    /// Registers every concrete controller in the API assembly. A controller whose dependencies
    /// have no mock still registers here but fails on resolution, which keeps the failure local to
    /// the test that asks for it.
    /// </summary>
    private static void RegisterControllers(IServiceCollection services)
    {
        var controllers = typeof(ApiBaseController).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t));

        foreach (var controller in controllers)
            services.AddTransient(controller);
    }
}
