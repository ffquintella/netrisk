using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Contracts.Importers;
using DAL.Entities;
using JetBrains.Annotations;
using Model;
using Model.Exceptions;
using ServerServices.Interfaces;
using Xunit;
using HostsService = ServerServices.Services.HostsService;
using HostServiceEntity = DAL.Entities.HostsService;

using ServerServices.Filtering;

namespace ServerServices.Tests.ServiceTests;

[TestSubject(typeof(HostsService))]
public class HostsServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IHostsService _svc;

    public HostsServiceInMemoryTest()
    {
        _svc = GetService<IHostsService>();
    }

    private static Host NewHost(int id, string ip = "10.0.0.1") => new()
    {
        Id = id, Ip = ip, Status = 1, Source = "manual", RegistrationDate = new DateTime(2026, 1, 1)
    };

    private static HostServiceEntity NewService(int id, int hostId, string name = "http", int? port = 80, string protocol = "tcp") => new()
    {
        Id = id, HostId = hostId, Name = name, Port = port, Protocol = protocol
    };

    [Fact]
    public void TestCreateAndGetAll()
    {
        _svc.Create(NewHost(0, "1.1.1.1"));
        _svc.Create(NewHost(0, "2.2.2.2"));

        Assert.Equal(2, _svc.GetAll().Count);
    }

    [Fact]
    public async Task TestCreateAsyncAndGetById()
    {
        var created = await _svc.CreateAsync(NewHost(0, "3.3.3.3"));

        Assert.Equal("3.3.3.3", _svc.GetById(created.Id).Ip);
        Assert.Throws<DataNotFoundException>(() => _svc.GetById(999));
    }

    [Fact]
    public async Task TestHostExists()
    {
        Seed(ctx => ctx.Hosts.Add(NewHost(1, "4.4.4.4")));

        Assert.True(await _svc.HostExistsAsync("4.4.4.4"));
        Assert.False(await _svc.HostExistsAsync("9.9.9.9"));
    }

    [Fact]
    public async Task TestGetByIp()
    {
        Seed(ctx => ctx.Hosts.Add(NewHost(1, "5.5.5.5")));

        Assert.Equal(1, _svc.GetByIp("5.5.5.5").Id);
        Assert.Equal(1, (await _svc.GetByIpAsync("5.5.5.5")).Id);
        Assert.Throws<DataNotFoundException>(() => _svc.GetByIp("0.0.0.0"));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetByIpAsync("0.0.0.0"));
    }

    [Fact]
    public void TestDelete()
    {
        Seed(ctx => ctx.Hosts.Add(NewHost(1)));

        _svc.Delete(1);

        Assert.Empty(_svc.GetAll());
        Assert.Throws<DataNotFoundException>(() => _svc.Delete(1));
    }

    [Fact]
    public void TestUpdate()
    {
        Seed(ctx => ctx.Hosts.Add(NewHost(1, "before")));

        var update = NewHost(1, "after");
        _svc.Update(update);

        Assert.Equal("after", _svc.GetById(1).Ip);
    }

    [Fact]
    public async Task TestUpdateAsyncAndValidation()
    {
        Seed(ctx => ctx.Hosts.Add(NewHost(1, "a")));
        await _svc.UpdateAsync(NewHost(1, "b"));
        Assert.Equal("b", _svc.GetById(1).Ip);

        Assert.Throws<ArgumentNullException>(() => _svc.Update(null!));
        Assert.Throws<ArgumentException>(() => _svc.Update(NewHost(0)));
        Assert.Throws<DataNotFoundException>(() => _svc.Update(NewHost(777)));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _svc.UpdateAsync(null!));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.UpdateAsync(NewHost(777)));
    }

    [Fact]
    public async Task TestGetFiltred()
    {
        Seed(ctx =>
        {
            ctx.Hosts.Add(NewHost(1, "a"));
            ctx.Hosts.Add(NewHost(2, "b"));
        });

        var (hosts, total) = await _svc.GetFiltredAsync(new ListQuery());

        Assert.Equal(2, total);
        Assert.Equal(2, hosts.Count);
    }

    [Fact]
    public void TestServiceLifecycle()
    {
        Seed(ctx => ctx.Hosts.Add(NewHost(1)));

        var created = _svc.CreateAndAddService(1, NewService(0, 1));
        Assert.NotNull(created);

        Assert.Single(_svc.GetHostServices(1));
        Assert.True(_svc.HostHasService(1, "http", 80, "tcp"));
        Assert.False(_svc.HostHasService(1, "ssh", 22, "tcp"));

        var fetched = _svc.GetHostService(1, created.Id);
        Assert.Equal("http", fetched.Name);

        _svc.UpdateService(1, NewService(created.Id, 1, name: "https", port: 443));
        Assert.Equal("https", _svc.GetHostService(1, created.Id).Name);

        _svc.DeleteService(1, created.Id);
        Assert.Empty(_svc.GetHostServices(1));
    }

    [Fact]
    public async Task TestServiceAsyncVariants()
    {
        Seed(ctx => ctx.Hosts.Add(NewHost(1)));

        await _svc.CreateAndAddServiceAsync(1, NewService(0, 1, name: "dns", port: 53, protocol: "udp"));

        Assert.True(await _svc.HostHasServiceAsync(1, "dns", 53, "udp"));
        var found = await _svc.FindServiceAsync(1, s => s.Name == "dns");
        Assert.Equal("dns", found.Name);
        var foundSync = _svc.FindService(1, s => s.Name == "dns");
        Assert.Equal("dns", foundSync.Name);
    }

    [Fact]
    public void TestGetVulnerabilities()
    {
        Seed(ctx =>
        {
            var host = NewHost(1);
            host.Vulnerabilities.Add(new Vulnerability
            {
                Id = 1, Title = "V", Status = 1,
                FirstDetection = new DateTime(2026, 1, 1), LastDetection = new DateTime(2026, 1, 1), DetectionCount = 1
            });
            ctx.Hosts.Add(host);
        });

        Assert.Single(_svc.GetVulnerabilities(1));
    }

    [Fact]
    public void TestHostNotFoundAndZeroIdGuards()
    {
        Assert.Throws<ArgumentException>(() => _svc.GetHostServices(0));
        Assert.Throws<DataNotFoundException>(() => _svc.GetHostServices(5));
        Assert.Throws<DataNotFoundException>(() => _svc.GetVulnerabilities(5));
        Assert.Throws<DataNotFoundException>(() => _svc.GetHostService(5, 1));
    }

    [Fact]
    public void TestGetHostServiceNotFound()
    {
        Seed(ctx => ctx.Hosts.Add(NewHost(1)));

        Assert.Throws<DataNotFoundException>(() => _svc.GetHostService(1, 999));
        Assert.Throws<DataNotFoundException>(() => _svc.DeleteService(1, 999));
    }

    // --- environments facet (S38 §5.2) --------------------------------------------------------

    private static Host HostIn(int id, string? environment, int? entityId = null)
    {
        var host = NewHost(id, $"10.1.0.{id}");
        host.Environment = environment;
        host.EntityId = entityId;
        return host;
    }

    [Fact]
    public async Task TestGetEnvironmentsReturnsDistinctNonBlankValuesInOrder()
    {
        Seed(ctx =>
        {
            ctx.Hosts.Add(HostIn(1, "Produção"));
            ctx.Hosts.Add(HostIn(2, "Homolog"));
            ctx.Hosts.Add(HostIn(3, "Produção"));
            ctx.Hosts.Add(HostIn(4, null));
            ctx.Hosts.Add(HostIn(5, ""));
            ctx.Hosts.Add(HostIn(6, "   "));
        });

        var environments = await _svc.GetEnvironmentsAsync();

        Assert.Equal(new[] { "Homolog", "Produção" }, environments);
    }

    [Fact]
    public async Task TestGetEnvironmentsIsEmptyWhenNoHostHasOne()
    {
        Seed(ctx => ctx.Hosts.Add(HostIn(1, null)));

        Assert.Empty(await _svc.GetEnvironmentsAsync());
    }

    /// <summary>A facet must not name an environment that only another entity's hosts are in.</summary>
    [Fact]
    public async Task TestGetEnvironmentsHonoursTheEntityScope()
    {
        SeedUnscoped(ctx =>
        {
            ctx.Hosts.Add(HostIn(1, "Produção", entityId: 10));
            ctx.Hosts.Add(HostIn(2, "Segredo", entityId: 20));
        });
        ScopeTo(10);

        Assert.Equal(new[] { "Produção" }, await _svc.GetEnvironmentsAsync());
    }

    // --- vulnerability summary (S38 §5.3) -----------------------------------------------------

    private static int _nextVulnerabilityId = 1000;

    private static Vulnerability Finding(int hostId, string? severity, IntStatus status, int? entityId = null) => new()
    {
        Id = System.Threading.Interlocked.Increment(ref _nextVulnerabilityId),
        Title = $"finding {severity}", HostId = hostId, Severity = severity, Status = (ushort)status,
        FirstDetection = new DateTime(2026, 1, 1), LastDetection = new DateTime(2026, 1, 1),
        DetectionCount = 1, EntityId = entityId
    };

    [Fact]
    public async Task TestTheSummaryCountsOpenFindingsBySeverity()
    {
        Seed(ctx =>
        {
            ctx.Hosts.Add(NewHost(1));
            ctx.Vulnerabilities.AddRange(
                Finding(1, "4", IntStatus.New),
                Finding(1, "4", IntStatus.Open),
                Finding(1, "3", IntStatus.Prioritized),
                Finding(1, "2", IntStatus.Reopened),
                Finding(1, "1", IntStatus.AwaitingFix),
                Finding(1, "0", IntStatus.New));
        });

        var summary = await _svc.GetVulnerabilitySummaryAsync(1);

        Assert.Equal(1, summary.HostId);
        Assert.Equal(2, summary.Open.Critical);
        Assert.Equal(1, summary.Open.High);
        Assert.Equal(1, summary.Open.Medium);
        Assert.Equal(1, summary.Open.Low);
        Assert.Equal(1, summary.Open.None);
        Assert.Equal(6, summary.Open.Total);
        Assert.Equal(6, summary.Total);
    }

    /// <summary>
    /// "Open" is the dashboard's definition, not a second one: every status in
    /// <see cref="Model.Status.ClosedStatuses"/> leaves the open counts, and still counts in the total.
    /// </summary>
    [Fact]
    public async Task TestClosedStatusesAreExcludedFromTheOpenCountsButNotFromTheTotal()
    {
        var closed = Model.Status.ClosedStatuses.Values.Select(s => (IntStatus)s).ToList();

        Seed(ctx =>
        {
            ctx.Hosts.Add(NewHost(1));
            foreach (var status in closed) ctx.Vulnerabilities.Add(Finding(1, "4", status));
            ctx.Vulnerabilities.Add(Finding(1, "4", IntStatus.Open));
        });

        var summary = await _svc.GetVulnerabilitySummaryAsync(1);

        Assert.Equal(1, summary.Open.Critical);
        Assert.Equal(1, summary.Open.Total);
        Assert.Equal(closed.Count + 1, summary.Total);
        Assert.Contains(IntStatus.Closed, closed);
        Assert.Contains(IntStatus.Fixed, closed);
        Assert.Contains(IntStatus.Rejected, closed);
        Assert.Contains(IntStatus.Retired, closed);
    }

    [Fact]
    public async Task TestAnUnparsableSeverityCountsAsNone()
    {
        Seed(ctx =>
        {
            ctx.Hosts.Add(NewHost(1));
            ctx.Vulnerabilities.AddRange(
                Finding(1, null, IntStatus.New),
                Finding(1, "", IntStatus.New),
                Finding(1, "high", IntStatus.New),
                Finding(1, "7", IntStatus.New),
                Finding(1, "-1", IntStatus.New),
                Finding(1, " 3 ", IntStatus.New));
        });

        var summary = await _svc.GetVulnerabilitySummaryAsync(1);

        Assert.Equal(5, summary.Open.None);
        Assert.Equal(1, summary.Open.High);
        Assert.Equal(6, summary.Total);
    }

    [Theory]
    [InlineData("0", NormalizedSeverity.None)]
    [InlineData("1", NormalizedSeverity.Low)]
    [InlineData("2", NormalizedSeverity.Medium)]
    [InlineData("3", NormalizedSeverity.High)]
    [InlineData("4", NormalizedSeverity.Critical)]
    [InlineData(" 4", NormalizedSeverity.Critical)]
    [InlineData("5", NormalizedSeverity.None)]
    [InlineData("4.0", NormalizedSeverity.None)]
    [InlineData("critical", NormalizedSeverity.None)]
    [InlineData("", NormalizedSeverity.None)]
    [InlineData(null, NormalizedSeverity.None)]
    public void TestParseSeverity(string? raw, NormalizedSeverity expected)
    {
        Assert.Equal(expected, HostsService.ParseSeverity(raw));
    }

    [Fact]
    public async Task TestAHostWithNoFindingsHasAnAllZeroSummary()
    {
        Seed(ctx => ctx.Hosts.Add(NewHost(1)));

        var summary = await _svc.GetVulnerabilitySummaryAsync(1);

        Assert.Equal(0, summary.Total);
        Assert.Equal(0, summary.Open.Total);
    }

    [Fact]
    public async Task TestTheSummaryOfAMissingHostIsNotFound()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetVulnerabilitySummaryAsync(404));
    }

    [Fact]
    public async Task TestTheSummaryOfAHostOutsideTheScopeIsNotFound()
    {
        SeedUnscoped(ctx =>
        {
            ctx.Hosts.Add(HostIn(1, null, entityId: 20));
            ctx.Vulnerabilities.Add(Finding(1, "4", IntStatus.New, entityId: 20));
        });
        ScopeTo(10);

        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetVulnerabilitySummaryAsync(1));
    }

    [Fact]
    public async Task TestTheBatchSummaryKeepsTheRequestedOrderAndDropsUnknownHosts()
    {
        Seed(ctx =>
        {
            ctx.Hosts.Add(NewHost(1, "10.2.0.1"));
            ctx.Hosts.Add(NewHost(2, "10.2.0.2"));
            ctx.Hosts.Add(NewHost(3, "10.2.0.3"));
            ctx.Vulnerabilities.AddRange(
                Finding(1, "4", IntStatus.New),
                Finding(2, "3", IntStatus.New),
                Finding(2, "3", IntStatus.Fixed),
                Finding(3, "1", IntStatus.New));
        });

        var summaries = await _svc.GetVulnerabilitySummariesAsync([3, 99, 1, 3, 2]);

        Assert.Equal(new[] { 3, 1, 2 }, summaries.Select(s => s.HostId).ToArray());
        Assert.Equal(1, summaries[0].Open.Low);
        Assert.Equal(1, summaries[1].Open.Critical);
        Assert.Equal(1, summaries[2].Open.High);
        Assert.Equal(2, summaries[2].Total);
    }

    /// <summary>
    /// Fifty hosts in one call. The in-memory provider cannot count SQL statements, so the
    /// one-grouped-query shape is held by the implementation, not asserted here; this pins the
    /// per-host answers and that a read writes nothing.
    /// </summary>
    [Fact]
    public async Task TestTheBatchSummaryAnswersFiftyHostsWithoutWriting()
    {
        Seed(ctx =>
        {
            for (var id = 1; id <= 50; id++)
            {
                ctx.Hosts.Add(NewHost(id, $"10.3.0.{id}"));
                ctx.Vulnerabilities.Add(Finding(id, (id % 5).ToString(), IntStatus.New));
            }
        });
        var saves = SaveChangesCount;

        var summaries = await _svc.GetVulnerabilitySummariesAsync(Enumerable.Range(1, 50).ToList());

        Assert.Equal(50, summaries.Count);
        Assert.All(summaries, s => Assert.Equal(1, s.Total));
        Assert.Equal(saves, SaveChangesCount);
    }

    [Fact]
    public async Task TestTheBatchSummaryOfNothingIsEmpty()
    {
        Assert.Empty(await _svc.GetVulnerabilitySummariesAsync([]));
    }

    [Fact]
    public async Task TestTheBatchSummaryRefusesMoreThanTheMaximum()
    {
        var ids = Enumerable.Range(1, IHostsService.MaxSummaryBatchSize + 1).ToList();

        await Assert.ThrowsAsync<ArgumentException>(() => _svc.GetVulnerabilitySummariesAsync(ids));
    }

    [Fact]
    public async Task TestTheBatchSummaryAcceptsExactlyTheMaximum()
    {
        var ids = Enumerable.Range(1, IHostsService.MaxSummaryBatchSize).ToList();

        Assert.Empty(await _svc.GetVulnerabilitySummariesAsync(ids));
    }
}
