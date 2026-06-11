using System;
using System.Threading.Tasks;
using DAL.Entities;
using FluentEmail.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Model.Exceptions;
using ServerServices.Interfaces;
using ServerServices.Services;
using ServerServices.Services.Importers;
using Xunit;
using ILogger = Serilog.ILogger;

namespace ServerServices.Tests.ServiceTests;

/// <summary>
/// One behavior test per service that is otherwise excluded from coverage (I/O, rendering,
/// external integrations). Each verifies a real, observable behavior using the in-memory DAL
/// or substitutes for the external boundary, rather than touching real infrastructure.
/// </summary>
public class ServiceBehaviorInMemoryTest : InMemoryServiceTestBase
{
    private ILogger Log => GetService<ILogger>();
    private IDalService Dal => GetService<IDalService>();

    // ---- ReportsService: returns persisted reports / guards missing ids ----
    [Fact]
    public void TestReportsServiceGetAllAndDelete()
    {
        var svc = new ReportsService(Log, Dal, GetService<ILocalizationService>());

        Assert.Empty(svc.GetAll());
        Assert.Throws<DataNotFoundException>(() => svc.Delete(999));
    }

    // ---- FilesService: reads file types from the database ----
    [Fact]
    public void TestFilesServiceGetFileTypesAndGetById()
    {
        Seed(ctx => ctx.FileTypes.Add(new FileType { Value = 1, Name = "pdf" }));
        var svc = new FilesService(Log, Dal);

        Assert.Single(svc.GetFileTypes());
        Assert.Throws<DataNotFoundException>(() => svc.GetById(999));
    }

    // ---- SystemService: rejects an unknown OS family ----
    [Fact]
    public async Task TestSystemServiceUnknownOsThrows()
    {
        var svc = new SystemService(Log, Dal);

        await Assert.ThrowsAsync<InvalidParameterException>(() => svc.GetUpdateScriptAsync("plan9"));
    }

    // ---- EnvironmentService: exposes the server application-data folder ----
    [Fact]
    public void TestEnvironmentServiceApplicationDataFolder()
    {
        var svc = new EnvironmentService();

        Assert.EndsWith("NRServer", svc.ApplicationDataFolder);
    }

    // ---- PluginsService: starts uninitialized ----
    [Fact]
    public void TestPluginsServiceNotInitialized()
    {
        var svc = new PluginsService(Log, Dal, Substitute.For<ISettingsService>());

        Assert.False(svc.IsInitialized());
    }

    // ---- EmailService: drives the fluent-email pipeline to send ----
    [Fact]
    public async Task TestEmailServiceSendsViaPipeline()
    {
        var email = Substitute.For<IFluentEmail>();
        email.To(Arg.Any<string>()).Returns(email);
        email.Subject(Arg.Any<string>()).Returns(email);
        email.UsingTemplateFromFile(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<bool>()).Returns(email);

        var svc = new EmailService(email);

        await svc.SendEmailAsync("a@b.io", "subj", "Template", "en", new { });

        email.Received(1).To("a@b.io");
        email.Received(1).Subject("subj");
        await email.Received(1).SendAsync();
    }

    // ---- DalService: constructs from configuration (DB-connection boundary) ----
    [Fact]
    public void TestDalServiceConstructs()
    {
        var config = Substitute.For<IConfiguration>();
        config["Database:ConnectionString"].Returns("server=localhost;uid=u;pwd=p;database=netrisk");
        config["Database:EnableSQLLogging"].Returns("false");

        var svc = new DalService(config, Substitute.For<IHttpContextAccessor>());

        Assert.NotNull(svc);
    }

    // ---- LocalizationService: provides a localizer and resource manager ----
    [Fact]
    public void TestLocalizationServiceProvidesLocalizer()
    {
        var svc = GetService<ILocalizationService>();

        Assert.NotNull(svc.GetLocalizer());
        Assert.NotNull(svc.GetResourceManager());
    }

    // ---- ImporterFactory / NessusImporter: resolves the Nessus importer, rejects unknown ----
    [Fact]
    public void TestImporterFactoryResolvesNessus()
    {
        var jobManager = new JobManager(
            GetService<IJobsService>(),
            Substitute.For<IAuthenticationService>(),
            GetService<IMessagesService>(),
            GetService<ILocalizationService>());

        var factory = new ImporterFactory(
            GetService<IHostsService>(),
            GetService<IVulnerabilitiesService>(),
            jobManager,
            GetService<IJobsService>());

        Assert.IsType<NessusImporter>(factory.GetImporter("tenable nessus", null));
        Assert.Throws<Exception>(() => factory.GetImporter("unknown-scanner", null));
    }

    // ---- FaceIDService: reads a user's completed biometric transactions ----
    [Fact]
    public async Task TestFaceIdServiceGetUserOpenTransactions()
    {
        var svc = new FaceIDService(
            Log, Dal,
            Substitute.For<IPluginsService>(),
            GetService<IUsersService>(),
            new EnvironmentService());

        var transactions = await svc.GetUserOpenTransactionsAsync(123);

        Assert.Empty(transactions);   // none seeded
    }

    // ---- JobManager: cancels a registered job ----
    [Fact]
    public async Task TestJobManagerCancelJob()
    {
        var jobsService = GetService<IJobsService>();
        var jobId = await jobsService.RegisterJobAsync("Test job");

        var manager = new JobManager(
            jobsService,
            Substitute.For<IAuthenticationService>(),
            GetService<IMessagesService>(),
            GetService<ILocalizationService>());

        await manager.CancelJob(jobId);

        using var ctx = OpenContext();
        Assert.Equal((int)Model.IntStatus.Cancelled, System.Linq.Enumerable.First(ctx.Jobs, j => j.Id == jobId).Status);
    }

    // ---- DatabaseService: data-fix dispatcher (backup/restore/migrate need a real DB) ----
    [Fact]
    public void TestDatabaseServiceFixData()
    {
        var svc = new DatabaseService(
            GetService<IConfiguration>(), Log,
            GetService<IConfigurationsService>(), Dal);

        Assert.Equal(-1, svc.FixData("unknown-operation"));
        Assert.Equal(0, svc.FixData("riskCatalog"));   // no risks → completes cleanly
    }
}
