using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Serilog;
using ServerServices.Services;

namespace BackgroundJobs.Tests.DI;

/// <summary>
/// Collaborators the jobs inherit from <c>BaseJob</c> but that a unit test does not exercise.
/// </summary>
public static class TestDoubles
{
    /// <summary>A logger that writes nowhere, so job output does not pollute the test run.</summary>
    public static ILogger Logger() => new LoggerConfiguration().CreateLogger();

    /// <summary>
    /// A real <see cref="DalService"/>. Its constructor only reads configuration — no connection is
    /// opened until <c>GetContext()</c> is called — so jobs that never touch the database can take
    /// this safely.
    /// </summary>
    public static DalService DalService()
    {
        var configuration = new ConfigurationBuilder()
            // string? because that is what AddInMemoryCollection takes: a configuration value is
            // legitimately absent, and Dictionary<string, string> is not the same type.
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = "server=localhost;database=netrisk_tests"
            })
            .Build();

        return new DalService(configuration, Substitute.For<IHttpContextAccessor>());
    }
}
