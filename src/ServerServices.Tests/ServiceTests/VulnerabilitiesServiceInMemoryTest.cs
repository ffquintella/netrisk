using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Model.Exceptions;
using ServerServices.Interfaces;
using ServerServices.Services;
using Sieve.Models;
using Xunit;

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

        var list = _svc.GetFiltred(new SieveModel(), out var total, includeFixRequests: true);

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
