using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Model.Exceptions;
using ServerServices.Interfaces;
using Sieve.Models;
using Xunit;
using HostsService = ServerServices.Services.HostsService;
using HostServiceEntity = DAL.Entities.HostsService;

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

        var (hosts, total) = await _svc.GetFiltredAsync(new SieveModel());

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
}
