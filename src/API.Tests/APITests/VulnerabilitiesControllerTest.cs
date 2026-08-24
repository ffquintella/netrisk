using System;
using System.Collections.Generic;
using System.IO;
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
using Model.Jobs;
using NSubstitute;
using ServerServices.Interfaces;
using ServerServices.Interfaces.Importers;
using Sieve.Exceptions;
using Sieve.Models;
using Xunit;

namespace API.Tests.APITests;

[TestSubject(typeof(VulnerabilitiesController))]
public class VulnerabilitiesControllerTest : BaseControllerTest, IDisposable
{
    private const int OkId = 1;
    private const int NotFoundId = 999;
    private const int ErrorId = 500;
    private const int InnerErrorId = 501;

    private readonly IVulnerabilitiesService _vulnerabilitiesService = Substitute.For<IVulnerabilitiesService>();
    private readonly IRisksService _risksService = Substitute.For<IRisksService>();
    private readonly IFilesService _filesService = Substitute.For<IFilesService>();
    private readonly IVulnerabilityImporterFactory _importerFactory = Substitute.For<IVulnerabilityImporterFactory>();
    private readonly IVulnerabilityImporter _importer = Substitute.For<IVulnerabilityImporter>();

    private readonly string _uploadDirectory;
    private readonly VulnerabilitiesController _controller;

    public VulnerabilitiesControllerTest()
    {
        var vulnerability = NewVulnerability(OkId);

        var all = new List<Vulnerability> { vulnerability, NewVulnerability(2) };

        _vulnerabilitiesService.GetAll().Returns(all);

        _vulnerabilitiesService.GetById(OkId, Arg.Any<bool>()).Returns(vulnerability);
        _vulnerabilitiesService.GetById(NotFoundId, Arg.Any<bool>())
            .Returns(_ => throw new DataNotFoundException("vulnerability", NotFoundId.ToString()));
        _vulnerabilitiesService.GetById(ErrorId, Arg.Any<bool>())
            .Returns(_ => throw new Exception("boom"));

        _vulnerabilitiesService.GetByIdAsync(OkId, Arg.Any<bool>()).Returns(vulnerability);
        _vulnerabilitiesService.GetByIdAsync(NotFoundId, Arg.Any<bool>())
            .Returns<Task<Vulnerability>>(_ => throw new DataNotFoundException("vulnerability", NotFoundId.ToString()));
        _vulnerabilitiesService.GetByIdAsync(ErrorId, Arg.Any<bool>())
            .Returns<Task<Vulnerability>>(_ => throw new Exception("boom"));

        _vulnerabilitiesService.Find("hash-ok").Returns(vulnerability);
        _vulnerabilitiesService.Find("hash-missing")
            .Returns(_ => throw new DataNotFoundException("vulnerability", "hash-missing"));
        _vulnerabilitiesService.Find("hash-boom").Returns(_ => throw new Exception("boom"));

        _vulnerabilitiesService.When(x => x.Delete(NotFoundId))
            .Do(_ => throw new DataNotFoundException("vulnerability", NotFoundId.ToString()));
        _vulnerabilitiesService.When(x => x.Delete(ErrorId)).Do(_ => throw new Exception("boom"));

        _vulnerabilitiesService.Create(Arg.Any<Vulnerability>())
            .Returns(ci =>
            {
                var created = ci.Arg<Vulnerability>();
                created.Id = 7;
                return created;
            });
        _vulnerabilitiesService.Create(Arg.Is<Vulnerability>(v => v.Title == "create-inner"))
            .Returns(_ => throw new Exception("outer", new Exception("inner")));
        _vulnerabilitiesService.Create(Arg.Is<Vulnerability>(v => v.Title == "create-boom"))
            .Returns(_ => throw new Exception("boom"));

        _vulnerabilitiesService.When(x => x.Update(Arg.Is<Vulnerability>(v => v.Id == ErrorId)))
            .Do(_ => throw new Exception("boom"));
        _vulnerabilitiesService.When(x => x.Update(Arg.Is<Vulnerability>(v => v.Id == InnerErrorId)))
            .Do(_ => throw new Exception("outer", new Exception("inner")));

        _vulnerabilitiesService.When(x => x.UpdateStatus(NotFoundId, Arg.Any<ushort>()))
            .Do(_ => throw new DataNotFoundException("vulnerability", NotFoundId.ToString()));
        _vulnerabilitiesService.When(x => x.UpdateStatus(ErrorId, Arg.Any<ushort>()))
            .Do(_ => throw new Exception("boom"));

        _vulnerabilitiesService.When(x => x.UpdateCommentsAsync(NotFoundId, Arg.Any<string>()))
            .Do(_ => throw new DataNotFoundException("vulnerability", NotFoundId.ToString()));
        _vulnerabilitiesService.When(x => x.UpdateCommentsAsync(ErrorId, Arg.Any<string>()))
            .Do(_ => throw new Exception("boom"));

        _vulnerabilitiesService.AddAction(OkId, 1, Arg.Any<NrAction>())
            .Returns(new NrAction { Id = 42, ObjectType = "vulnerability", Message = "done" });
        _vulnerabilitiesService.AddAction(NotFoundId, 1, Arg.Any<NrAction>())
            .Returns(_ => throw new DataNotFoundException("vulnerability", NotFoundId.ToString()));
        _vulnerabilitiesService.AddAction(ErrorId, 1, Arg.Any<NrAction>())
            .Returns(_ => throw new Exception("boom"));

        _vulnerabilitiesService.AssociateRisksAsync(NotFoundId, Arg.Any<List<int>>())
            .Returns<Task>(_ => throw new DataNotFoundException("vulnerability", NotFoundId.ToString()));
        _vulnerabilitiesService.AssociateRisksAsync(ErrorId, Arg.Any<List<int>>())
            .Returns<Task>(_ => throw new Exception("boom"));
        _vulnerabilitiesService.AssociateRisksAsync(InnerErrorId, Arg.Any<List<int>>())
            .Returns<Task>(_ => throw new Exception("outer", new Exception("inner")));

        _risksService.GetRisksScoringAsync(Arg.Any<List<int>>()).Returns(new List<RiskScoring>
        {
            new() { Id = 10, ScoringMethod = 1, CalculatedRisk = 5 },
            new() { Id = 11, ScoringMethod = 1, CalculatedRisk = 3 }
        });

        _uploadDirectory = Path.Combine(Path.GetTempPath(), "netrisk-vuln-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_uploadDirectory);
        File.WriteAllText(Path.Combine(_uploadDirectory, "good.dat"), "<NessusClientData_v2/>");
        File.WriteAllText(Path.Combine(_uploadDirectory, "broken.dat"), "<NessusClientData_v2/>");

        _filesService.GetUploadDirectory().Returns(_uploadDirectory);

        // The legacy factory is still a constructor dependency but is no longer on the import path:
        // import/nessus/{fileId} runs the Track 3 pipeline, so the job id now comes from the job
        // manager and the importer factory is only wired here to keep the controller constructible.
        _importerFactory.GetImporter("tenable nessus", Arg.Any<User>()).Returns(_importer);

        _controller = Build(_vulnerabilitiesService);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_uploadDirectory)) Directory.Delete(_uploadDirectory, true);
        }
        catch (Exception)
        {
            // best effort clean up of the throw-away upload directory
        }
    }

    private static Vulnerability NewVulnerability(int id)
    {
        return new Vulnerability
        {
            Id = id,
            Title = "Vulnerability " + id,
            Severity = "high",
            Score = 7.5,
            Status = (ushort)Model.IntStatus.Open,
            Risks = new List<Risk> { new() { Id = 10 }, new() { Id = 11 } },
            Actions = new List<NrAction>
            {
                new() { Id = 1, ObjectType = "vulnerability", Message = "created" }
            }
        };
    }

    private VulnerabilitiesController Build(IVulnerabilitiesService vulnerabilitiesService,
        Action<IServiceCollection>? configure = null)
    {
        var controller = ResolveController<VulnerabilitiesController>(s =>
        {
            s.AddSingleton(vulnerabilitiesService);
            s.AddSingleton(_risksService);
            s.AddSingleton(_filesService);
            s.AddSingleton(_importerFactory);
            configure?.Invoke(s);
        });

        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        return controller;
    }

    // ---------------- GetAll ----------------

    [Fact]
    public void TestGetAll()
    {
        var result = _controller.GetAll();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<Vulnerability>>(ok.Value);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void TestGetAllReturnsServerErrorOnException()
    {
        var service = Substitute.For<IVulnerabilitiesService>();
        service.GetAll().Returns(_ => throw new Exception("boom"));

        var result = Build(service).GetAll();

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    // ---------------- GetLastScanDateAsync ----------------

    [Fact]
    public async Task TestGetLastScanDateAsync()
    {
        var service = Substitute.For<IVulnerabilitiesService>();
        service.GetLastScanDateAsync().Returns((DateTime?)new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));

        var result = await Build(service).GetLastScanDateAsync();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), Assert.IsType<DateTime>(ok.Value));
    }

    [Fact]
    public async Task TestGetLastScanDateReturnsServerErrorOnException()
    {
        var service = Substitute.For<IVulnerabilitiesService>();
        service.GetLastScanDateAsync().Returns<Task<DateTime?>>(_ => throw new Exception("boom"));

        var result = await Build(service).GetLastScanDateAsync();

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    // ---------------- GetFiltered ----------------

    [Fact]
    public void TestGetFiltered()
    {
        var service = Substitute.For<IVulnerabilitiesService>();
        service.GetFiltred(null, out _, false).ReturnsForAnyArgs(ci =>
        {
            ci[1] = 12;
            return new List<Vulnerability> { NewVulnerability(1) };
        });

        var controller = Build(service);
        var result = controller.GetFiltered(new SieveModel());

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single(Assert.IsType<List<Vulnerability>>(ok.Value));
        Assert.Equal("12", controller.Response.Headers["X-Total-Count"].ToString());
    }

    [Fact]
    public void TestGetFilteredIncludingFixRequests()
    {
        var service = Substitute.For<IVulnerabilitiesService>();
        service.GetFiltred(null, out _, false).ReturnsForAnyArgs(ci =>
        {
            ci[1] = 0;
            return new List<Vulnerability>();
        });

        var result = Build(service).GetFiltered(new SieveModel(), "en-US", true);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty(Assert.IsType<List<Vulnerability>>(Assert.IsType<OkObjectResult>(result.Result).Value));
    }

    [Fact]
    public void TestGetFilteredWithInvalidCultureThrows()
    {
        Assert.Throws<BadRequestException>(() => _controller.GetFiltered(new SieveModel(), "zz-ZZ"));
    }

    [Fact]
    public void TestGetFilteredReturnsConflictOnUnknownSieveMethod()
    {
        var service = Substitute.For<IVulnerabilitiesService>();
        service.GetFiltred(null, out _, false)
            .ReturnsForAnyArgs<List<Vulnerability>>(_ => throw new SieveMethodNotFoundException("nope", "no method"));

        var result = Build(service).GetFiltered(new SieveModel());

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(409, status.StatusCode.GetValueOrDefault());
    }

    [Fact]
    public void TestGetFilteredReturnsBadRequestOnSieveException()
    {
        var service = Substitute.For<IVulnerabilitiesService>();
        service.GetFiltred(null, out _, false)
            .ReturnsForAnyArgs<List<Vulnerability>>(_ => throw new SieveException("bad filter"));

        var result = Build(service).GetFiltered(new SieveModel());

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, status.StatusCode.GetValueOrDefault());
    }

    [Fact]
    public void TestGetFilteredReturnsServerErrorOnException()
    {
        var service = Substitute.For<IVulnerabilitiesService>();
        service.GetFiltred(null, out _, false)
            .ReturnsForAnyArgs<List<Vulnerability>>(_ => throw new Exception("boom"));

        var result = Build(service).GetFiltered(new SieveModel());

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    // ---------------- FindVulnerability ----------------

    [Fact]
    public void TestFindVulnerability()
    {
        var result = _controller.FindVulnerability("hash-ok");
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(OkId, Assert.IsType<Vulnerability>(ok.Value).Id);
    }

    [Fact]
    public void TestFindVulnerabilityNotFound()
    {
        var result = _controller.FindVulnerability("hash-missing");
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public void TestFindVulnerabilityReturnsServerErrorOnException()
    {
        var result = _controller.FindVulnerability("hash-boom");
        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    // ---------------- GetOne ----------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestGetOne(bool includeDetails)
    {
        var result = _controller.GetOne(OkId, includeDetails);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(OkId, Assert.IsType<Vulnerability>(ok.Value).Id);
    }

    [Fact]
    public void TestGetOneNotFound()
    {
        Assert.IsType<NotFoundResult>(_controller.GetOne(NotFoundId).Result);
    }

    [Fact]
    public void TestGetOneReturnsServerErrorOnException()
    {
        var status = Assert.IsType<StatusCodeResult>(_controller.GetOne(ErrorId).Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    // ---------------- DeleteOne ----------------

    [Fact]
    public void TestDeleteOne()
    {
        Assert.IsType<OkResult>(_controller.DeleteOne(OkId));
        _vulnerabilitiesService.Received(1).Delete(OkId);
    }

    [Fact]
    public void TestDeleteOneNotFound()
    {
        Assert.IsType<NotFoundResult>(_controller.DeleteOne(NotFoundId));
    }

    [Fact]
    public void TestDeleteOneReturnsServerErrorOnException()
    {
        var status = Assert.IsType<StatusCodeResult>(_controller.DeleteOne(ErrorId));
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    // ---------------- Create ----------------

    [Fact]
    public void TestCreate()
    {
        var result = _controller.Create(new Vulnerability { Id = 123, Title = "new one" });

        var created = Assert.IsType<CreatedResult>(result.Result);
        var value = Assert.IsType<Vulnerability>(created.Value);
        Assert.Equal(7, value.Id);
        Assert.Equal("/Vulnerabilities/7", created.Location);
    }

    [Fact]
    public void TestCreateReturnsServerErrorOnExceptionWithInnerException()
    {
        var result = _controller.Create(new Vulnerability { Title = "create-inner" });
        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    [Fact]
    public void TestCreateReturnsServerErrorOnException()
    {
        var result = _controller.Create(new Vulnerability { Title = "create-boom" });
        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    // ---------------- ImportNessusVulnerabilities ----------------

    [Fact]
    public async Task TestImportNessusVulnerabilities()
    {
        var result = await _controller.ImportNessusVulnerabilities("good");

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var job = Assert.IsType<ImportJobCreationResult>(ok.Value);

        // The compatibility alias now runs the Track 3 pipeline, so the job id comes from the job
        // manager and the response also carries the scan_imports row to poll.
        Assert.Equal(Mock.MockedJobManager.JobId, job.JobId);
        Assert.Equal(1, job.ImportId);
        Assert.False(job.IsReplay);
        Assert.True(job.Success);
        Assert.Equal("Import started", job.Message);
        Assert.Equal((int)Model.IntStatus.Running, job.JobStatus);
    }

    [Fact]
    public async Task TestImportNessusVulnerabilitiesRejectsUnsafeFileId()
    {
        var result = await _controller.ImportNessusVulnerabilities("../../etc/passwd");
        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Invalid fileId", bad.Value);
    }

    [Fact]
    public async Task TestImportNessusVulnerabilitiesFileNotFound()
    {
        var result = await _controller.ImportNessusVulnerabilities("missing-file");
        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal("File not found", notFound.Value);
    }

    [Fact]
    public async Task TestImportNessusVulnerabilitiesReturnsServerErrorOnException()
    {
        // The import itself is asynchronous now — a malformed report fails on the scan_imports row,
        // not in the response. What still produces a 500 is a failure to start the job at all.
        var failingJobManager = Substitute.For<IJobManager>();
        failingJobManager.RunAndRegisterJob(Arg.Any<IJobRunner>())
            .Returns<Task<int>>(_ => throw new Exception("boom"));

        var controller = Build(_vulnerabilitiesService, s => s.AddSingleton(failingJobManager));

        var result = await controller.ImportNessusVulnerabilities("broken");
        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    // ---------------- Update ----------------

    [Fact]
    public void TestUpdate()
    {
        var vulnerability = NewVulnerability(OkId);
        var result = _controller.Update(OkId, vulnerability);

        Assert.IsType<OkResult>(result.Result);
        Assert.Null(vulnerability.FixTeam);
        Assert.Null(vulnerability.Host);
        _vulnerabilitiesService.Received(1).Update(vulnerability);
    }

    [Fact]
    public void TestUpdateWithNullBodyThrows()
    {
        Assert.Throws<ArgumentNullException>(() => _controller.Update(OkId, null));
    }

    [Fact]
    public void TestUpdateWithMismatchedIdThrows()
    {
        Assert.Throws<ArgumentException>(() => _controller.Update(2, NewVulnerability(OkId)));
    }

    [Fact]
    public void TestUpdateReturnsServerErrorOnException()
    {
        var status = Assert.IsType<StatusCodeResult>(_controller.Update(ErrorId, NewVulnerability(ErrorId)).Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    [Fact]
    public void TestUpdateReturnsServerErrorOnExceptionWithInnerException()
    {
        var status = Assert.IsType<StatusCodeResult>(
            _controller.Update(InnerErrorId, NewVulnerability(InnerErrorId)).Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    // ---------------- GetRisksScoringAsync ----------------

    [Fact]
    public async Task TestGetRisksScoringAsync()
    {
        var result = await _controller.GetRisksScoringAsync(OkId);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(2, Assert.IsType<List<RiskScoring>>(ok.Value).Count);
    }

    [Fact]
    public async Task TestGetRisksScoringAsyncNotFound()
    {
        var result = await _controller.GetRisksScoringAsync(NotFoundId);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task TestGetRisksScoringAsyncReturnsServerErrorOnException()
    {
        var result = await _controller.GetRisksScoringAsync(ErrorId);
        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    // ---------------- GetStatus ----------------

    [Fact]
    public void TestGetStatus()
    {
        var result = _controller.GetStatus(OkId);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal((ushort)Model.IntStatus.Open, Assert.IsType<ushort>(ok.Value));
    }

    [Fact]
    public void TestGetStatusNotFound()
    {
        Assert.IsType<NotFoundResult>(_controller.GetStatus(NotFoundId).Result);
    }

    [Fact]
    public void TestGetStatusReturnsServerErrorOnException()
    {
        var status = Assert.IsType<StatusCodeResult>(_controller.GetStatus(ErrorId).Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    // ---------------- GetActions ----------------

    [Fact]
    public void TestGetActions()
    {
        var result = _controller.GetActions(OkId);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single(Assert.IsType<List<NrAction>>(ok.Value));
    }

    [Fact]
    public void TestGetActionsNotFound()
    {
        Assert.IsType<NotFoundResult>(_controller.GetActions(NotFoundId).Result);
    }

    [Fact]
    public void TestGetActionsReturnsServerErrorOnException()
    {
        var status = Assert.IsType<StatusCodeResult>(_controller.GetActions(ErrorId).Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    // ---------------- AddAction ----------------

    [Fact]
    public void TestAddAction()
    {
        var result = _controller.AddAction(OkId, new NrAction { ObjectType = "vulnerability", Message = "hello" });
        var created = Assert.IsType<CreatedResult>(result.Result);
        Assert.Equal(42, Assert.IsType<NrAction>(created.Value).Id);
    }

    [Fact]
    public void TestAddActionNotFound()
    {
        var result = _controller.AddAction(NotFoundId, new NrAction { ObjectType = "vulnerability" });
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public void TestAddActionReturnsServerErrorOnException()
    {
        var result = _controller.AddAction(ErrorId, new NrAction { ObjectType = "vulnerability" });
        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    // ---------------- UpdateStatus ----------------

    [Fact]
    public void TestUpdateStatus()
    {
        Assert.IsType<OkResult>(_controller.UpdateStatus(OkId, 4).Result);
        _vulnerabilitiesService.Received(1).UpdateStatus(OkId, 4);
    }

    [Fact]
    public void TestUpdateStatusNotFound()
    {
        Assert.IsType<NotFoundResult>(_controller.UpdateStatus(NotFoundId, 4).Result);
    }

    [Fact]
    public void TestUpdateStatusReturnsServerErrorOnException()
    {
        var status = Assert.IsType<StatusCodeResult>(_controller.UpdateStatus(ErrorId, 4).Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    // ---------------- UpdateComments ----------------

    [Fact]
    public void TestUpdateComments()
    {
        Assert.IsType<OkResult>(_controller.UpdateComments(OkId, new CommentDto { Text = "a comment" }).Result);
        _vulnerabilitiesService.Received(1).UpdateCommentsAsync(OkId, "a comment");
    }

    [Fact]
    public void TestUpdateCommentsNotFound()
    {
        var result = _controller.UpdateComments(NotFoundId, new CommentDto { Text = "a comment" });
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public void TestUpdateCommentsReturnsServerErrorOnException()
    {
        var result = _controller.UpdateComments(ErrorId, new CommentDto { Text = "a comment" });
        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    // ---------------- AssociateRisks ----------------

    [Fact]
    public async Task TestAssociateRisks()
    {
        var riskIds = new List<int> { 10, 11 };
        var result = await _controller.AssociateRisks(OkId, riskIds);

        Assert.IsType<OkResult>(result);
        await _vulnerabilitiesService.Received(1).AssociateRisksAsync(OkId, riskIds);
    }

    [Fact]
    public async Task TestAssociateRisksNotFound()
    {
        var result = await _controller.AssociateRisks(NotFoundId, new List<int> { 10 });
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task TestAssociateRisksReturnsServerErrorOnException()
    {
        var result = await _controller.AssociateRisks(ErrorId, new List<int> { 10 });
        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    [Fact]
    public async Task TestAssociateRisksReturnsServerErrorOnExceptionWithInnerException()
    {
        var result = await _controller.AssociateRisks(InnerErrorId, new List<int> { 10 });
        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }
}
