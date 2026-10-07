using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

using ServerServices.Filtering;

namespace ServerServices.Tests.ServiceTests;

[TestSubject(typeof(VulnerabilitiesService))]
public class VulnerabilitiesServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IVulnerabilitiesService _svc;

    public VulnerabilitiesServiceInMemoryTest()
    {
        _svc = GetService<IVulnerabilitiesService>();
    }

    private static Vulnerability NewVuln(int id, string title = "V", ushort status = 1, string? hash = null) => new()
    {
        Id = id,
        Title = title,
        Status = status,
        ImportHash = hash,
        FirstDetection = new DateTime(2026, 1, 1),
        LastDetection = new DateTime(2026, 1, 2),
        DetectionCount = 1
    };

    private static Risk NewRisk(int id) => new()
    {
        Id = id, Status = "Open", Subject = "S", ReferenceId = "R", Assessment = "",
        Notes = "", RiskCatalogMapping = "", ThreatCatalogMapping = ""
    };

    [Fact]
    public void TestCreateAndGetAll()
    {
        _svc.Create(NewVuln(0, "Created"));
        _svc.Create(NewVuln(0, "Created2"));

        var all = _svc.GetAll();

        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task TestCreateAsyncAndGetById()
    {
        var created = await _svc.CreateAsync(NewVuln(0, "Async"));

        var fetched = await _svc.GetByIdAsync(created.Id);
        Assert.Equal("Async", fetched.Title);
        Assert.Equal("Async", _svc.GetById(created.Id).Title);
    }

    [Fact]
    public async Task TestGetByIdNotFoundThrows()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetByIdAsync(999));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetByIdAsync(999, includeDetails: true));
    }

    [Fact]
    public void TestDelete()
    {
        Seed(ctx => ctx.Vulnerabilities.Add(NewVuln(1)));

        _svc.Delete(1);

        Assert.Empty(_svc.GetAll());
        Assert.Throws<DataNotFoundException>(() => _svc.Delete(1));
    }

    [Fact]
    public void TestUpdate()
    {
        Seed(ctx => ctx.Vulnerabilities.Add(NewVuln(1, "Before")));

        _svc.Update(NewVuln(1, "After"));

        Assert.Equal("After", _svc.GetById(1).Title);
    }

    [Fact]
    public void TestUpdateValidation()
    {
        Assert.Throws<ArgumentNullException>(() => _svc.Update(null!));
        Assert.Throws<ArgumentException>(() => _svc.Update(NewVuln(0)));
        Assert.Throws<DataNotFoundException>(() => _svc.Update(NewVuln(777)));
    }

    // ---- GitHub #79: the server/application classification ----

    [Fact]
    public void TestCreateWithoutASourceTypeStoresUnknown()
    {
        var created = _svc.Create(NewVuln(0, "Unclassified"));

        Assert.Equal(VulnerabilitySourceType.Unknown, _svc.GetById(created.Id).SourceType);
    }

    [Theory]
    [InlineData(VulnerabilitySourceType.Server)]
    [InlineData(VulnerabilitySourceType.Application)]
    public async Task TestCreateRoundTripsTheSourceType(VulnerabilitySourceType sourceType)
    {
        var vulnerability = NewVuln(0, "Classified");
        vulnerability.SourceType = sourceType;
        var created = _svc.Create(vulnerability);

        var asyncVulnerability = NewVuln(0, "Classified async");
        asyncVulnerability.SourceType = sourceType;
        var createdAsync = await _svc.CreateAsync(asyncVulnerability);

        Assert.Equal(sourceType, _svc.GetById(created.Id).SourceType);
        Assert.Equal(sourceType, (await _svc.GetByIdAsync(createdAsync.Id)).SourceType);
    }

    [Fact]
    public async Task TestUpdateChangesTheSourceType()
    {
        Seed(ctx =>
        {
            ctx.Vulnerabilities.Add(NewVuln(1, "Sync"));
            ctx.Vulnerabilities.Add(NewVuln(2, "Async"));
        });

        var sync = NewVuln(1, "Sync");
        sync.SourceType = VulnerabilitySourceType.Server;
        _svc.Update(sync);

        var viaAsync = NewVuln(2, "Async");
        viaAsync.SourceType = VulnerabilitySourceType.Application;
        await _svc.UpdateAsync(viaAsync);

        Assert.Equal(VulnerabilitySourceType.Server, _svc.GetById(1).SourceType);
        Assert.Equal(VulnerabilitySourceType.Application, _svc.GetById(2).SourceType);

        // …and back: an analyst can withdraw a classification.
        var reset = NewVuln(1, "Sync");
        reset.SourceType = VulnerabilitySourceType.Unknown;
        _svc.Update(reset);
        Assert.Equal(VulnerabilitySourceType.Unknown, _svc.GetById(1).SourceType);
    }

    [Fact]
    public async Task TestCreateRejectsAnUndeclaredSourceType()
    {
        var vulnerability = NewVuln(0, "Bogus");
        vulnerability.SourceType = (VulnerabilitySourceType)7;

        var ex = Assert.Throws<InvalidParameterException>(() => _svc.Create(vulnerability));
        Assert.Equal(nameof(Vulnerability.SourceType), ex.ParameterName);
        await Assert.ThrowsAsync<InvalidParameterException>(() => _svc.CreateAsync(vulnerability));

        Assert.Empty(_svc.GetAll());
    }

    [Fact]
    public async Task TestUpdateRejectsAnUndeclaredSourceType()
    {
        Seed(ctx => ctx.Vulnerabilities.Add(NewVuln(1, "Before")));

        var bogus = NewVuln(1, "After");
        bogus.SourceType = (VulnerabilitySourceType)(-1);

        Assert.Throws<InvalidParameterException>(() => _svc.Update(bogus));
        // UpdateAsync logs and swallows its other failures; this one must reach the caller.
        await Assert.ThrowsAsync<InvalidParameterException>(() => _svc.UpdateAsync(bogus));

        var stored = _svc.GetById(1);
        Assert.Equal("Before", stored.Title);
        Assert.Equal(VulnerabilitySourceType.Unknown, stored.SourceType);
    }

    [Fact]
    public void TestUpdateStatus()
    {
        Seed(ctx => ctx.Vulnerabilities.Add(NewVuln(1, status: 1)));

        _svc.UpdateStatus(1, 5);

        Assert.Equal(5, _svc.GetById(1).Status);
        Assert.Throws<DataNotFoundException>(() => _svc.UpdateStatus(99, 1));
    }

    [Fact]
    public void TestGetFiltred()
    {
        Seed(ctx =>
        {
            ctx.Vulnerabilities.Add(NewVuln(1));
            ctx.Vulnerabilities.Add(NewVuln(2));
        });

        var list = _svc.GetFiltred(new ListQuery(), out var total, includeFixRequests: true);

        Assert.Equal(2, total);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task TestAssociateRisks()
    {
        Seed(ctx =>
        {
            ctx.Vulnerabilities.Add(NewVuln(1));
            ctx.Risks.Add(NewRisk(10));
            ctx.Risks.Add(NewRisk(20));
        });

        await _svc.AssociateRisksAsync(1, new List<int> { 10, 20 });

        var vuln = await _svc.GetByIdAsync(1, includeDetails: true);
        Assert.Equal(2, vuln.Risks.Count);
    }

    [Fact]
    public async Task TestAssociateRisksNotFound()
    {
        Seed(ctx => ctx.Vulnerabilities.Add(NewVuln(1)));

        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.AssociateRisksAsync(1, new List<int> { 404 }));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.AssociateRisksAsync(99, new List<int>()));
    }

    [Fact]
    public void TestFindByHash()
    {
        Seed(ctx => ctx.Vulnerabilities.Add(NewVuln(1, hash: "abc")));

        Assert.Equal(1, _svc.Find("abc").Id);
        Assert.Throws<DataNotFoundException>(() => _svc.Find("missing"));
    }

    [Fact]
    public async Task TestFindAsyncByHash()
    {
        Seed(ctx => ctx.Vulnerabilities.Add(NewVuln(1, hash: "xyz")));

        var found = await _svc.FindAsync("xyz");
        Assert.NotNull(found);
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.FindAsync("none"));
    }

    [Fact]
    public async Task TestAddAction()
    {
        Seed(ctx => ctx.Vulnerabilities.Add(NewVuln(1)));

        var action = await _svc.AddActionAsync(1, 7, new NrAction { ObjectType = "vulnerability", DateTime = new DateTime(2026, 1, 1) });

        Assert.Equal(7, action.UserId);
        Assert.Throws<DataNotFoundException>(() =>
            _svc.AddAction(99, 1, new NrAction { ObjectType = "vulnerability" }));
    }

    [Fact]
    public async Task TestGetVulnerabilitiesByHostId()
    {
        Seed(ctx =>
        {
            var v1 = NewVuln(1); v1.HostId = 100;
            var v2 = NewVuln(2); v2.HostId = 200;
            ctx.Vulnerabilities.Add(v1);
            ctx.Vulnerabilities.Add(v2);
        });

        var list = await _svc.GetVulnerabilitiesByHostIdAsync(100);

        Assert.Single(list);
    }

    [Fact]
    public async Task TestGetLastScanDate()
    {
        Seed(ctx =>
        {
            var v1 = NewVuln(1); v1.LastDetection = new DateTime(2026, 1, 1);
            var v2 = NewVuln(2); v2.LastDetection = new DateTime(2026, 6, 1);
            ctx.Vulnerabilities.Add(v1);
            ctx.Vulnerabilities.Add(v2);
        });

        var last = await _svc.GetLastScanDateAsync();

        Assert.Equal(new DateTime(2026, 6, 1), last);
    }

    [Fact]
    public async Task TestGetLastScanDateEmptyThrows()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetLastScanDateAsync());
    }
}
