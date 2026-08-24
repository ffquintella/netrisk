using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using API.Controllers;
using API.Exceptions;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.DTO;
using Model.Exceptions;
using NSubstitute;
using ServerServices.Interfaces;
using Sieve.Exceptions;
using Sieve.Models;
using Xunit;
using Host = DAL.Entities.Host;
using HostsServiceEntity = DAL.Entities.HostsService;

namespace API.Tests.APITests;

[TestSubject(typeof(HostsController))]
public class HostsControllerTest : BaseControllerTest
{
    private readonly IHostsService _hostsService = Substitute.For<IHostsService>();
    private readonly HostsController _controller;

    public HostsControllerTest()
    {
        _controller = Build(_hostsService);
    }

    /// <summary>
    /// Builds a controller over a caller supplied <see cref="IHostsService"/> double and gives it a
    /// live <see cref="ControllerContext"/> so actions that touch <c>Response</c> can run.
    /// </summary>
    private static HostsController Build(IHostsService hostsService)
    {
        var controller = ResolveController<HostsController>(s => s.AddSingleton(hostsService));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    private static (HostsController Controller, IHostsService Service) NewController()
    {
        var service = Substitute.For<IHostsService>();
        return (Build(service), service);
    }

    private static Host SampleHost(int id = 1)
    {
        return new Host
        {
            Id = id,
            Ip = "10.0.0.1",
            HostName = "host-" + id,
            Source = "test",
            Status = 1
        };
    }

    private static HostsServiceEntity SampleService(int id = 1, int hostId = 1)
    {
        return new HostsServiceEntity
        {
            Id = id,
            HostId = hostId,
            Name = "http",
            Protocol = "tcp",
            Port = 80
        };
    }

    #region GetAll

    [Fact]
    public void TestGetAll()
    {
        _hostsService.GetAll().Returns(new List<Host> { SampleHost(1), SampleHost(2) });

        var result = _controller.GetAll();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var hosts = Assert.IsType<List<Host>>(ok.Value);
        Assert.Equal(2, hosts.Count);
    }

    [Fact]
    public void TestGetAllInternalError()
    {
        var (controller, service) = NewController();
        service.GetAll().Returns(_ => throw new Exception("boom"));

        var result = controller.GetAll();

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    #endregion

    #region GetFiltered

    [Fact]
    public async Task TestGetFiltered()
    {
        _hostsService.GetFiltredAsync(Arg.Any<SieveModel>())
            .Returns(Task.FromResult(new Tuple<List<Host>, int>(new List<Host> { SampleHost(1) }, 7)));

        var result = await _controller.GetFiltered(new SieveModel());

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var hosts = Assert.IsType<List<Host>>(ok.Value);
        Assert.Single(hosts);
        Assert.Equal("7", _controller.Response.Headers["X-Total-Count"].ToString());
    }

    [Fact]
    public async Task TestGetFilteredInvalidFilterReturnsConflict()
    {
        var (controller, service) = NewController();
        service.GetFiltredAsync(Arg.Any<SieveModel>())
            .Returns<Task<Tuple<List<Host>, int>>>(_ => throw new SieveMethodNotFoundException("BadMethod", "method not found"));

        var result = await controller.GetFiltered(new SieveModel());

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(409, status.StatusCode);
    }

    [Fact]
    public async Task TestGetFilteredSieveErrorReturnsBadRequest()
    {
        var (controller, service) = NewController();
        service.GetFiltredAsync(Arg.Any<SieveModel>())
            .Returns<Task<Tuple<List<Host>, int>>>(_ => throw new SieveException("filter error"));

        var result = await controller.GetFiltered(new SieveModel());

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, status.StatusCode);
    }

    [Fact]
    public async Task TestGetFilteredInternalError()
    {
        var (controller, service) = NewController();
        service.GetFiltredAsync(Arg.Any<SieveModel>())
            .Returns<Task<Tuple<List<Host>, int>>>(_ => throw new Exception("boom"));

        var result = await controller.GetFiltered(new SieveModel());

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    [Fact]
    public async Task TestGetFilteredInvalidCultureThrows()
    {
        await Assert.ThrowsAsync<BadRequestException>(
            () => _controller.GetFiltered(new SieveModel(), "zz-ZZ"));
    }

    #endregion

    #region GetOne

    [Fact]
    public void TestGetOne()
    {
        _hostsService.GetById(1).Returns(SampleHost(1));

        var result = _controller.GetOne(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var host = Assert.IsType<Host>(ok.Value);
        Assert.Equal(1, host.Id);
    }

    [Fact]
    public void TestGetOneNotFound()
    {
        _hostsService.GetById(999).Returns(_ => throw new DataNotFoundException("hosts", "999"));

        var result = _controller.GetOne(999);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public void TestGetOneInternalError()
    {
        _hostsService.GetById(500).Returns(_ => throw new Exception("boom"));

        var result = _controller.GetOne(500);

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    #endregion

    #region GetByIp

    [Fact]
    public void TestGetByIp()
    {
        _hostsService.GetByIp("10.0.0.1").Returns(SampleHost(1));

        var result = _controller.GetByIp("10.0.0.1");

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var host = Assert.IsType<Host>(ok.Value);
        Assert.Equal("10.0.0.1", host.Ip);
    }

    [Fact]
    public void TestGetByIpWithoutParameterReturnsBadRequest()
    {
        var result = _controller.GetByIp(null);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void TestGetByIpNotFound()
    {
        _hostsService.GetByIp("10.0.0.9").Returns(_ => throw new DataNotFoundException("hosts", "10.0.0.9"));

        var result = _controller.GetByIp("10.0.0.9");

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public void TestGetByIpInternalError()
    {
        _hostsService.GetByIp("boom").Returns(_ => throw new Exception("boom"));

        var result = _controller.GetByIp("boom");

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    #endregion

    #region DeleteOne

    [Fact]
    public void TestDeleteOne()
    {
        var result = _controller.DeleteOne(1);

        Assert.IsType<OkResult>(result);
        _hostsService.Received(1).Delete(1);
    }

    [Fact]
    public void TestDeleteOneNotFound()
    {
        _hostsService.When(x => x.Delete(999)).Do(_ => throw new DataNotFoundException("hosts", "999"));

        var result = _controller.DeleteOne(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void TestDeleteOneInternalError()
    {
        _hostsService.When(x => x.Delete(500)).Do(_ => throw new Exception("boom"));

        var result = _controller.DeleteOne(500);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    #endregion

    #region Create

    [Fact]
    public void TestCreate()
    {
        _hostsService.Create(Arg.Any<Host>()).Returns(SampleHost(42));

        var result = _controller.Create(SampleHost(77));

        var created = Assert.IsType<CreatedResult>(result.Result);
        var host = Assert.IsType<Host>(created.Value);
        Assert.Equal(42, host.Id);
        Assert.Equal("/Hosts/42", created.Location);
    }

    [Fact]
    public void TestCreateInternalError()
    {
        var (controller, service) = NewController();
        service.Create(Arg.Any<Host>()).Returns(_ => throw new Exception("boom"));

        var result = controller.Create(SampleHost());

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    #endregion

    #region Update

    [Fact]
    public void TestUpdate()
    {
        var host = SampleHost(3);

        var result = _controller.Update(3, host);

        Assert.IsType<OkResult>(result.Result);
        _hostsService.Received(1).Update(host);
    }

    [Fact]
    public void TestUpdateNullHostThrows()
    {
        Assert.Throws<ArgumentNullException>(() => { _controller.Update(1, null); });
    }

    [Fact]
    public void TestUpdateIdMismatchThrows()
    {
        Assert.Throws<ArgumentException>(() => { _controller.Update(2, SampleHost(3)); });
    }

    [Fact]
    public void TestUpdateInternalError()
    {
        var (controller, service) = NewController();
        service.When(x => x.Update(Arg.Any<Host>())).Do(_ => throw new Exception("boom"));

        var result = controller.Update(1, SampleHost(1));

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    #endregion

    #region GetServices

    [Fact]
    public void TestGetServices()
    {
        _hostsService.GetHostServices(1)
            .Returns(new List<HostsServiceEntity> { SampleService(1), SampleService(2) });

        var result = _controller.GetServices(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var services = Assert.IsType<List<HostsServiceEntity>>(ok.Value);
        Assert.Equal(2, services.Count);
    }

    [Fact]
    public void TestGetServicesNotFound()
    {
        _hostsService.GetHostServices(999).Returns(_ => throw new DataNotFoundException("hosts", "999"));

        var result = _controller.GetServices(999);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public void TestGetServicesInternalError()
    {
        _hostsService.GetHostServices(500).Returns(_ => throw new Exception("boom"));

        var result = _controller.GetServices(500);

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    #endregion

    #region GetVulnerabilities

    [Fact]
    public void TestGetVulnerabilities()
    {
        _hostsService.GetVulnerabilities(1).Returns(new List<Vulnerability>
        {
            new() { Id = 1, Title = "vuln 1" }
        });

        var result = _controller.GetVulnerabilities(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var vulnerabilities = Assert.IsType<List<Vulnerability>>(ok.Value);
        Assert.Single(vulnerabilities);
    }

    [Fact]
    public void TestGetVulnerabilitiesNotFound()
    {
        _hostsService.GetVulnerabilities(999).Returns(_ => throw new DataNotFoundException("hosts", "999"));

        var result = _controller.GetVulnerabilities(999);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public void TestGetVulnerabilitiesInternalError()
    {
        _hostsService.GetVulnerabilities(500).Returns(_ => throw new Exception("boom"));

        var result = _controller.GetVulnerabilities(500);

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    #endregion

    #region HostHasServices

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TestHostHasServices(bool exists)
    {
        var (controller, service) = NewController();
        service.HostHasService(1, "http", 80, "tcp").Returns(exists);

        var result = controller.HostHasServices(1, "http", "tcp", 80);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(exists, Assert.IsType<bool>(ok.Value));
    }

    [Fact]
    public void TestHostHasServicesNotFound()
    {
        _hostsService.HostHasService(999, "http", 80, "tcp")
            .Returns(_ => throw new DataNotFoundException("hosts", "999"));

        var result = _controller.HostHasServices(999, "http", "tcp", 80);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public void TestHostHasServicesInternalError()
    {
        _hostsService.HostHasService(500, "http", 80, "tcp")
            .Returns(_ => throw new Exception("boom"));

        var result = _controller.HostHasServices(500, "http", "tcp", 80);

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    #endregion

    #region FindService

    [Fact]
    public void TestFindService()
    {
        _hostsService.FindService(1, Arg.Any<Expression<Func<HostsServiceEntity, bool>>>())
            .Returns(SampleService(5));

        var result = _controller.FindService(1, "http", "tcp", 80);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var service = Assert.IsType<HostsServiceEntity>(ok.Value);
        Assert.Equal(5, service.Id);
    }

    [Fact]
    public void TestFindServiceNotFound()
    {
        _hostsService.FindService(999, Arg.Any<Expression<Func<HostsServiceEntity, bool>>>())
            .Returns(_ => throw new DataNotFoundException("hosts", "999"));

        var result = _controller.FindService(999, "http", "tcp", 80);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public void TestFindServiceInternalError()
    {
        _hostsService.FindService(500, Arg.Any<Expression<Func<HostsServiceEntity, bool>>>())
            .Returns(_ => throw new Exception("boom"));

        var result = _controller.FindService(500, "http", "tcp", 80);

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    #endregion

    #region GetService

    [Fact]
    public void TestGetService()
    {
        _hostsService.GetHostService(1, 2).Returns(SampleService(2));

        var result = _controller.GetService(1, 2);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var service = Assert.IsType<HostsServiceEntity>(ok.Value);
        Assert.Equal(2, service.Id);
    }

    [Fact]
    public void TestGetServiceNotFound()
    {
        _hostsService.GetHostService(999, 1).Returns(_ => throw new DataNotFoundException("hosts", "999"));

        var result = _controller.GetService(999, 1);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public void TestGetServiceInternalError()
    {
        _hostsService.GetHostService(500, 1).Returns(_ => throw new Exception("boom"));

        var result = _controller.GetService(500, 1);

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    #endregion

    #region CreateService

    private static HostsServiceDto SampleDto()
    {
        return new HostsServiceDto
        {
            Id = 0,
            Name = "http",
            Port = 80,
            Protocol = "tcp"
        };
    }

    [Fact]
    public void TestCreateService()
    {
        var (controller, service) = NewController();
        service.HostHasService(1, "http", 80, "tcp").Returns(false);
        service.CreateAndAddService(1, Arg.Any<HostsServiceEntity>()).Returns(SampleService(9));

        var result = controller.CreateService(1, SampleDto());

        var created = Assert.IsType<CreatedResult>(result.Result);
        var newService = Assert.IsType<HostsServiceEntity>(created.Value);
        Assert.Equal(9, newService.Id);
        Assert.Equal("1/Services/9", created.Location);
    }

    [Fact]
    public void TestCreateServiceAlreadyExistsReturnsBadRequest()
    {
        var (controller, service) = NewController();
        service.HostHasService(1, "http", 80, "tcp").Returns(true);

        var result = controller.CreateService(1, SampleDto());

        Assert.IsType<BadRequestObjectResult>(result.Result);
        service.DidNotReceive().CreateAndAddService(Arg.Any<int>(), Arg.Any<HostsServiceEntity>());
    }

    [Fact]
    public void TestCreateServiceNotFound()
    {
        var (controller, service) = NewController();
        service.HostHasService(999, "http", 80, "tcp")
            .Returns(_ => throw new DataNotFoundException("hosts", "999"));

        var result = controller.CreateService(999, SampleDto());

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public void TestCreateServiceInternalError()
    {
        var (controller, service) = NewController();
        service.HostHasService(500, "http", 80, "tcp").Returns(false);
        service.CreateAndAddService(500, Arg.Any<HostsServiceEntity>())
            .Returns(_ => throw new Exception("boom"));

        var result = controller.CreateService(500, SampleDto());

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    #endregion

    #region DeleteService

    [Fact]
    public void TestDeleteService()
    {
        var result = _controller.DeleteService(1, 2);

        Assert.IsType<OkResult>(result);
        _hostsService.Received(1).DeleteService(1, 2);
    }

    [Fact]
    public void TestDeleteServiceNotFound()
    {
        _hostsService.When(x => x.DeleteService(999, 1))
            .Do(_ => throw new DataNotFoundException("hosts", "999"));

        var result = _controller.DeleteService(999, 1);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void TestDeleteServiceInternalError()
    {
        _hostsService.When(x => x.DeleteService(500, 1)).Do(_ => throw new Exception("boom"));

        var result = _controller.DeleteService(500, 1);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    #endregion

    #region UpdateService

    [Fact]
    public void TestUpdateService()
    {
        var result = _controller.UpdateService(1, 2, SampleDto());

        Assert.IsType<OkResult>(result);
        _hostsService.Received(1).UpdateService(1, Arg.Is<HostsServiceEntity>(s => s.Id == 2 && s.HostId == 1));
    }

    [Fact]
    public void TestUpdateServiceNotFound()
    {
        var (controller, service) = NewController();
        service.When(x => x.UpdateService(999, Arg.Any<HostsServiceEntity>()))
            .Do(_ => throw new DataNotFoundException("hosts", "999"));

        var result = controller.UpdateService(999, 1, SampleDto());

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void TestUpdateServiceInternalError()
    {
        var (controller, service) = NewController();
        service.When(x => x.UpdateService(500, Arg.Any<HostsServiceEntity>()))
            .Do(_ => throw new Exception("boom"));

        var result = controller.UpdateService(500, 1, SampleDto());

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    #endregion
}
