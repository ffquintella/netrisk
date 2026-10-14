using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Context;
using Hangfire;
using Hangfire.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Serilog;
using Serilog.Extensions.Logging;
using ServerServices.Interfaces;
using ServerServices.Services;
using ILogger = Serilog.ILogger;

namespace BackgroundJobs.Tests.DI;

/// <summary>
/// The job host's own container — <see cref="ConfigurationManager.RegisterServices"/>, the registrations
/// production runs — with three substitutions, each for a collaborator a test must not reach:
/// <list type="bullet">
/// <item>the database: <see cref="InMemoryJobHostDalService"/> keeps the real <see cref="DalService.GetContext"/>
/// and swaps only the provider, so the scope and identity every context carries still come from the
/// registered <see cref="IHttpContextAccessor"/>;</item>
/// <item>the outbound HTTP client, so no job or service can call a real host;</item>
/// <item>the secret protector, whose production constructor reads — and on a fresh machine creates — the
/// install's master key.</item>
/// </list>
/// The accessor is not substituted. It is the thing under test.
/// </summary>
public static class JobHostContainer
{
    public static ServiceProvider Build(string databaseName)
    {
        var logger = new LoggerConfiguration().CreateLogger();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = "server=localhost;database=netrisk_tests"
            })
            .Build();

        var services = new ServiceCollection();

        // What Program.cs adds before it hands over to the configuration manager.
        services.AddSingleton<ILogger>(logger);
        services.AddSingleton<ILoggerFactory>(new SerilogLoggerFactory(logger));

        ConfigurationManager.RegisterServices(services, configuration);

        services.Replace(ServiceDescriptor.Singleton<DalService>(sp =>
            new InMemoryJobHostDalService(configuration, sp.GetRequiredService<IHttpContextAccessor>(),
                databaseName)));
        services.Replace(ServiceDescriptor.Singleton<IDalService>(sp => sp.GetRequiredService<DalService>()));

        services.Replace(ServiceDescriptor.Singleton(Substitute.For<IOutboundHttpClient>()));
        services.Replace(ServiceDescriptor.Singleton(Substitute.For<ISecretProtector>()));

        // As production builds it (ConfigureHangFire): no scope validation, jobs resolved from the root.
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// The recurring jobs <see cref="JobsManager.ConfigureScheduledJobs(IServiceProvider, IRecurringJobManager)"/>
    /// schedules, read from the method itself rather than from a list a test keeps in step with it.
    /// </summary>
    public static IReadOnlyList<ScheduledJob> Schedule(IServiceProvider sp)
    {
        var recorder = new RecordingRecurringJobManager();
        JobsManager.ConfigureScheduledJobs(sp, recorder);
        return recorder.Jobs;
    }
}

/// <summary>One <c>AddOrUpdate</c> the job host makes at start-up.</summary>
public sealed record ScheduledJob(string Id, Type Type, string Cron);

/// <summary>An <see cref="IRecurringJobManager"/> that records what is scheduled and runs nothing.</summary>
public sealed class RecordingRecurringJobManager : IRecurringJobManager
{
    private readonly List<ScheduledJob> _jobs = [];

    public IReadOnlyList<ScheduledJob> Jobs => _jobs;

    public void AddOrUpdate(string recurringJobId, Job job, string cronExpression, RecurringJobOptions options) =>
        _jobs.Add(new ScheduledJob(recurringJobId, job.Type, cronExpression));

    public void Trigger(string recurringJobId) => throw new NotSupportedException();

    public void RemoveIfExists(string recurringJobId) => throw new NotSupportedException();
}

/// <summary>
/// The real <see cref="DalService"/> on the EF Core in-memory provider. Only
/// <see cref="GetDbContextOptionsBuilder"/> is overridden: <see cref="DalService.GetContext"/>,
/// <see cref="DalService.GetCurrentEntityScope"/>, the user id and the audit actor are the production code,
/// reading the production accessor.
/// </summary>
public sealed class InMemoryJobHostDalService(
    IConfiguration configuration,
    IHttpContextAccessor accessor,
    string databaseName) : DalService(configuration, accessor)
{
    protected override DbContextOptionsBuilder<NRDbContext> GetDbContextOptionsBuilder() =>
        new DbContextOptionsBuilder<NRDbContext>()
            .UseInMemoryDatabase(databaseName)
            .EnableSensitiveDataLogging();
}
