using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ServerServices.Security;
using Xunit;

namespace API.Tests.Security;

/// <summary>
/// T237 (S39 D-01) gave the Hangfire job host's principal unrestricted entity scope. These tests are what keep
/// that grant inside the job host: no request can obtain it.
///
/// The grant is <see cref="BackgroundServiceHttpContextAccessor.WithUnrestrictedEntityScope"/> — the background
/// identity plus the <c>scope=global</c> claim. A request could reach it only if an HTTP host resolved its
/// <see cref="IHttpContextAccessor"/> to that principal, so: the API's composition root registers the request
/// accessor and nothing else; no HTTP host's source names the background accessor at all; and nothing in
/// production but the job host names the grant. The claim itself is not new — the API's handlers issue it to an
/// administrator only, which <c>ApiTokenAuthenticationHandlerTest</c> and
/// <c>BackgroundServiceHttpContextAccessorTest</c> (an impostor of the background identity reads <c>DenyAll</c>)
/// pin from the other side.
/// </summary>
[TestSubject(typeof(BackgroundServiceHttpContextAccessor))]
public class BackgroundScopeContainmentTest
{
    /// <summary>The source projects that serve HTTP requests.</summary>
    private static readonly string[] HttpHosts = ["API", "WebSite", "RiskPortal"];

    /// <summary>The only production files allowed to name the grant: its definition and its one caller.</summary>
    private static readonly string[] GrantAllowlist =
    [
        Path.Combine("ServerServices", "Security", "BackgroundServiceHttpContextAccessor.cs"),
        Path.Combine("BackgroundJobs", "ConfigurationManager.cs"),
    ];

    private static string SrcRoot()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        Assert.True(Directory.Exists(Path.Combine(root, "API")), root);
        return root;
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    /// <summary>Test projects are named <c>*.Tests</c>, <c>*.IntegrationTests</c> or <c>*.UiTests</c>.</summary>
    private static bool IsTestProject(string root, string path)
    {
        var project = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar)[0];
        return project.EndsWith("Tests", StringComparison.Ordinal);
    }

    private static IEnumerable<string> ProductionSources(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsBuildOutput(f) && !IsTestProject(root, f));

    [Fact]
    public void TheApiCompositionRootRegistersOnlyTheRequestAccessor()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["email:from"] = "no-reply@netrisk.test",
                ["email:smtp:server"] = "localhost",
                ["email:smtp:port"] = "25",
                ["languages:availableLocales:0"] = "en-US",
                ["languages:defaultLocale"] = "en-US",
            })
            .Build();

        var services = new ServiceCollection();
        ServicesBootstrapper.RegisterServices(services, config);

        var accessors = services.Where(d => d.ServiceType == typeof(IHttpContextAccessor)).ToList();

        Assert.NotEmpty(accessors);
        Assert.All(accessors, d =>
        {
            // A type, never a factory or an instance: either could hand a request the background principal.
            Assert.Equal(typeof(HttpContextAccessor), d.ImplementationType);
            Assert.Null(d.ImplementationFactory);
            Assert.Null(d.ImplementationInstance);
        });
    }

    [Fact]
    public void NoHttpHostNamesTheBackgroundAccessor()
    {
        var root = SrcRoot();

        var offenders = HttpHosts
            .SelectMany(host => Directory.EnumerateFiles(Path.Combine(root, host), "*.cs", SearchOption.AllDirectories))
            .Where(f => !IsBuildOutput(f))
            .Where(f => File.ReadAllText(f).Contains(nameof(BackgroundServiceHttpContextAccessor), StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(root, f))
            .ToList();

        Assert.True(offenders.Count == 0,
            "An HTTP host names the background accessor; a request could run as the job host:\n  " +
            string.Join("\n  ", offenders));
    }

    [Fact]
    public void OnlyTheJobHostGrantsUnrestrictedBackgroundScope()
    {
        var root = SrcRoot();

        var callers = ProductionSources(root)
            .Where(f => File.ReadAllText(f).Contains(
                nameof(BackgroundServiceHttpContextAccessor.WithUnrestrictedEntityScope), StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(root, f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(GrantAllowlist.OrderBy(f => f, StringComparer.Ordinal), callers);
    }

    // --- T237 review: jobs a request triggers -------------------------------------------------------------

    /// <summary>
    /// The job types a request may enqueue or schedule, each one reviewed to read under the <b>requester's</b> scope as it
    /// stands at run time (<see cref="ServerServices.Security.IUserEntityScopeResolver"/>, through
    /// <c>IDalService.GetContextFor</c>) rather than the job host's global grant. Adding a type here is the review:
    /// <see cref="EveryRequestEnqueuedJobReadsUnderTheRequestersScope"/> then holds it to the same shape.
    /// </summary>
    private static readonly Type[] RequestEnqueuedJobs = [typeof(ServerServices.Services.ScheduledReportJob)];

    private static readonly System.Text.RegularExpressions.Regex TypedHangfireCall = new(
        @"\b(?:BackgroundJob|RecurringJob)\s*\.\s*\w+\s*<\s*(?<type>\w+)\s*>", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex UntypedHangfireCall = new(
        @"\b(?:BackgroundJob\s*\.\s*(?:Enqueue|Schedule|ContinueJobWith)|RecurringJob\s*\.\s*AddOrUpdate)\s*\(|\b(?:IBackgroundJobClient|IRecurringJobManager|BackgroundJobClient|RecurringJobManager)\b",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// The review's blocker: <c>ReportSchedulesController</c> enqueued <c>ScheduledReportJob</c> on behalf of any user,
    /// and the job read every entity's incidents under the job host's grant and mailed them to recipients the user chose.
    /// Outside the job host, Hangfire may only be reached through its static facades with the job type spelled out, and
    /// every such type must be one of <see cref="RequestEnqueuedJobs"/> — so a new user-triggered job fails here until it
    /// is reviewed for the same pattern. The job host's own schedule (<c>JobsManager</c>) is system work and out of this.
    /// </summary>
    [Fact]
    public void EveryJobARequestCanEnqueueIsOnTheReviewedList()
    {
        var root = SrcRoot();
        var allowed = RequestEnqueuedJobs.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        var offenders = new List<string>();
        foreach (var file in ProductionSources(root))
        {
            var relative = Path.GetRelativePath(root, file);
            if (relative.StartsWith("BackgroundJobs" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;

            var source = File.ReadAllText(file);

            offenders.AddRange(TypedHangfireCall.Matches(source)
                .Select(m => m.Groups["type"].Value)
                .Where(type => !allowed.Contains(type))
                .Select(type => $"{relative}: enqueues {type}, which is not a reviewed request-enqueued job"));

            offenders.AddRange(UntypedHangfireCall.Matches(source)
                .Select(m => $"{relative}: '{m.Value.Trim()}' reaches Hangfire without naming a reviewed job type"));
        }

        Assert.True(offenders.Count == 0,
            "A request can trigger a job the review has not seen; it would run under the job host's global scope:\n  " +
            string.Join("\n  ", offenders));
    }

    /// <summary>
    /// ...and each reviewed job takes the resolver and reads through a context scoped to what it resolves. A shape check,
    /// not a proof — <c>ServerServices.Tests/Security/ScheduledReportScopeTest</c> is the behavioural one for the report.
    /// </summary>
    [Fact]
    public void EveryRequestEnqueuedJobReadsUnderTheRequestersScope()
    {
        var root = SrcRoot();

        foreach (var job in RequestEnqueuedJobs)
        {
            Assert.Contains(job.GetConstructors().SelectMany(c => c.GetParameters()),
                p => p.ParameterType == typeof(ServerServices.Security.IUserEntityScopeResolver));

            var source = Directory.EnumerateFiles(root, job.Name + ".cs", SearchOption.AllDirectories)
                .Where(f => !IsBuildOutput(f) && !IsTestProject(root, f))
                .Select(File.ReadAllText)
                .Single();

            Assert.Contains(".GetContextFor(", source, StringComparison.Ordinal);
        }
    }

    /// <summary>The structural half: the API cannot call into the job host's composition, because it does not load it.</summary>
    [Fact]
    public void TheApiDoesNotReferenceTheJobHost()
    {
        var referenced = typeof(ServicesBootstrapper).Assembly.GetReferencedAssemblies().Select(a => a.Name);

        Assert.DoesNotContain("BackgroundJobs", referenced);
    }
}
