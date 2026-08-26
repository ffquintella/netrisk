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
        var svc = new ReportsService(Log, Dal, GetService<ILocalizationService>(), GetService<IQuestPdfRenderingService>());

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
        var email = FluentEmailSubstitute();

        var svc = new EmailService(email);

        await svc.SendEmailAsync("a@b.io", "subj", "Template", "en", new { });

        email.Received(1).To("a@b.io");
        email.Received(1).Subject("subj");
        await email.Received(1).SendAsync();
    }

    /// <summary>
    /// <c>IFluentEmail</c> is a builder whose recipient list accumulates, and one instance is injected
    /// per service — so before the address lists were cleared per send, a second message went to the
    /// first message's recipient as well. For a notification channel that means a Slack-outage fallback
    /// email reaching whoever happened to be notified before.
    /// </summary>
    [Fact]
    public async Task TestEmailServiceDoesNotAccumulateRecipientsAcrossSends()
    {
        var email = FluentEmailSubstitute();

        var svc = new EmailService(email);

        await svc.SendEmailAsync("first@b.io", "one", "Template", "en", new { });

        // Stands in for the first send having left its recipient on the shared builder.
        email.Data.ToAddresses.Add(new FluentEmail.Core.Models.Address("first@b.io"));

        await svc.SendEmailAsync("second@b.io", "two", "Template", "en", new { });

        // The stale recipient is gone, and the second send addressed only its own. (The substitute's
        // To() does not itself populate Data, so the assertion is about the clearing, which is the
        // behaviour under test.)
        Assert.DoesNotContain(email.Data.ToAddresses, address => address.EmailAddress == "first@b.io");
        email.Received(1).To("second@b.io");
    }

    /// <summary>
    /// Track 4.1.2 — the notification channel needs a way in that does not go through a Razor template,
    /// and a send the sender refuses has to surface as a failure rather than as a delivered notification.
    /// </summary>
    [Fact]
    public async Task TestEmailServiceSendsAPreRenderedNotification()
    {
        var email = FluentEmailSubstitute();

        var svc = new EmailService(email);

        await svc.SendNotificationAsync("a@b.io", "subj", "<p>body</p>", "body");

        email.Received(1).To("a@b.io");
        email.Received(1).Body("<p>body</p>", true);
        email.Received(1).PlaintextAlternativeBody("body");
        await email.Received(1).SendAsync();
    }

    [Fact]
    public async Task TestEmailServiceThrowsWhenTheSenderRefusesTheMessage()
    {
        var email = FluentEmailSubstitute(successful: false);

        var svc = new EmailService(email);

        // FluentEmail reports a refused send as an unsuccessful response rather than an exception, so a
        // service that only caught exceptions would report a rejected notification as delivered.
        var thrown = await Assert.ThrowsAsync<Exception>(
            () => svc.SendNotificationAsync("a@b.io", "subj", "<p>body</p>"));

        Assert.Contains("Error sending mail", thrown.Message);
    }

    private static IFluentEmail FluentEmailSubstitute(bool successful = true)
    {
        var email = Substitute.For<IFluentEmail>();

        email.Data.Returns(new FluentEmail.Core.Models.EmailData());
        email.To(Arg.Any<string>()).Returns(email);
        email.Subject(Arg.Any<string>()).Returns(email);
        email.Body(Arg.Any<string>(), Arg.Any<bool>()).Returns(email);
        email.PlaintextAlternativeBody(Arg.Any<string>()).Returns(email);
        email.UsingTemplateFromFile(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<bool>()).Returns(email);

        var response = new FluentEmail.Core.Models.SendResponse();
        if (!successful) response.ErrorMessages.Add("The relay refused the message.");
        email.SendAsync().Returns(Task.FromResult(response));

        return email;
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
            new EnvironmentService(),
            // Track 8 / finding NR-2026-032: the biometric template and the signature seed are now
            // protected on write, so the service needs a protector. The container's one is over a
            // fixed root secret, so nothing writes to the install's key file.
            GetService<ISecretProtector>());

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
