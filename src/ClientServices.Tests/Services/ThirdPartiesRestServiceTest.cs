using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.ThirdParties;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Stage 9.10 (S51 §7) — <see cref="ThirdPartiesRestService"/> over <see cref="StubRestBackend"/>, so every URL it builds,
/// every body and query string it sends and every status branch runs for real. A refusal (a supplier in use, a terminated
/// relationship, a malformed SBOM, a body too large) keeps the server's sentence.
/// </summary>
[TestSubject(typeof(ThirdPartiesRestService))]
public class ThirdPartiesRestServiceTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IThirdPartiesService _service;

    public ThirdPartiesRestServiceTest()
    {
        _service = ResolveWith<IThirdPartiesService>(_backend);
    }

    private static ThirdPartyDto Party(int id = 7) => new()
    {
        Id = id, Name = "Acme Cloud", Status = ThirdPartyStatus.Active, IsCloudProvider = true,
        Links = [new ThirdPartyLinkDto { EntityId = 20, Kind = ThirdPartyLinkKind.ItService, EntityName = "Student portal" }],
        Hecvat = new HecvatResultDto { State = HecvatState.Incomplete, ExpectedCount = 4, AnsweredCount = 3 },
        Concentration = new ConcentrationEntryDto { DependentCriticalProcessCount = 2, Share = 1m },
        Findings = [new ThirdPartyFindingDto { Code = ThirdPartyFindingCode.HecvatIncomplete, Message = "Incomplete." }]
    };

    private static ThirdPartyAssessmentDto Assessment() => new()
    {
        Id = 8, ThirdPartyId = 7, Variant = HecvatVariant.Full, FrameworkVersion = "3.06", ExpectedQuestionCount = 4,
        Result = new HecvatResultDto { State = HecvatState.Conforming, Score = 0.9m },
        Answers = [new HecvatAnswerDto { QuestionId = "HFIH-01", Answer = HecvatAnswer.Yes, PreferredAnswer = HecvatAnswer.Yes }]
    };

    private static ThirdPartySbomDto Sbom() => new()
    {
        Id = 9, ThirdPartyId = 7, ComponentName = "Moodle", Format = SbomFormat.SpdxJson, ComponentCount = 1,
        Components = [new SbomComponentDto { Name = "rails", Version = "7.1.3", Purl = "pkg:gem/rails@7.1.3" }]
    };

    private bool BodyHas(string fragment) => _backend.LastRequest.Body.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    private bool QueryHas(string fragment) => _backend.LastRequest.Query.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    // --- reads ------------------------------------------------------------------------------

    [Fact]
    public async Task TestTheListSendsItsFiltersOnlyWhenAsked()
    {
        _backend.OnGet("/ThirdParties", new[]
        {
            new ThirdPartySummaryDto { Id = 7, Name = "Acme Cloud", Status = ThirdPartyStatus.Active, HecvatState = HecvatState.Conforming }
        });

        var all = await _service.GetThirdPartiesAsync();
        Assert.Equal((7, HecvatState.Conforming), (Assert.Single(all).Id, all[0].HecvatState));
        Assert.True(string.IsNullOrEmpty(_backend.LastRequest.Query));

        await _service.GetThirdPartiesAsync(ThirdPartyStatus.Terminated, true);
        Assert.True(QueryHas("status=4"));
        Assert.True(QueryHas("includeTerminated=true"));
    }

    [Fact]
    public async Task TestTheReadsHitTheirRoutes()
    {
        _backend.OnGet("/ThirdParties/7", Party());
        _backend.OnGet("/ThirdParties/Concentration", new ThirdPartyConcentrationReportDto
        {
            CriticalProcessCount = 2,
            Cloud = new ConcentrationDimensionDto
            {
                Dimension = ConcentrationDimension.Cloud, MaxShare = 0.5m,
                Entries = [new ConcentrationEntryDto { ThirdPartyId = 7, DependentCriticalProcessCount = 1, ReachedThroughSubprocessing = true }]
            }
        });
        _backend.OnGet("/ThirdParties/ByEntity/50", new[]
            { new EntityThirdPartyDto { ThirdPartyId = 7, Name = "Acme Cloud", Kind = ThirdPartyLinkKind.Data } });
        _backend.OnGet("/ThirdParties/7/Assessments/8", Assessment());
        _backend.OnGet("/ThirdParties/7/Sboms/9", Sbom());
        _backend.OnGet("/ThirdParties/7/History", new[]
            { new AuditLog { Id = 1, EntityType = "ThirdParty", EntityId = 7, Field = "", Actor = "user:1" } });

        var party = await _service.GetThirdPartyAsync(7);
        Assert.Equal((HecvatState.Incomplete, 2, ThirdPartyFindingCode.HecvatIncomplete),
            (party.Hecvat.State, party.Concentration.DependentCriticalProcessCount, party.Findings.Single().Code));

        var report = await _service.GetConcentrationAsync();
        Assert.Equal((2, 0.5m, true), (report.CriticalProcessCount, report.Cloud.MaxShare!.Value,
            report.Cloud.Entries.Single().ReachedThroughSubprocessing));

        Assert.Equal(ThirdPartyLinkKind.Data, Assert.Single(await _service.GetByEntityAsync(50)).Kind);
        Assert.Equal((HecvatState.Conforming, 0.9m), ((await _service.GetAssessmentAsync(7, 8)).Result.State,
            (await _service.GetAssessmentAsync(7, 8)).Result.Score!.Value));
        Assert.Equal("pkg:gem/rails@7.1.3", Assert.Single((await _service.GetSbomAsync(7, 9)).Components).Purl);

        Assert.Equal(7, Assert.Single(await _service.GetHistoryAsync(7, 25)).EntityId);
        Assert.True(QueryHas("limit=25"));
    }

    // --- writes -----------------------------------------------------------------------------

    [Fact]
    public async Task TestTheRecordWritesSendTheirBodies()
    {
        _backend.OnPost("/ThirdParties", Party());
        _backend.OnPut("/ThirdParties/7", Party());
        _backend.OnStatus(Method.Delete, "/ThirdParties/7", HttpStatusCode.NoContent);

        await _service.CreateAsync(new ThirdPartyRequest { Name = "Acme Cloud", ContractedRtoMinutes = 240, IsCloudProvider = true });
        Assert.True(_backend.Sent(Method.Post, "/ThirdParties"));
        Assert.True(BodyHas("\"name\":\"Acme Cloud\"") || BodyHas("\"Name\":\"Acme Cloud\""));
        Assert.True(BodyHas("240"));

        await _service.UpdateAsync(7, new ThirdPartyRequest { Name = "Acme", RightToAudit = true });
        Assert.True(_backend.Sent(Method.Put, "/ThirdParties/7"));
        Assert.True(BodyHas("RightToAudit") || BodyHas("rightToAudit"));

        await _service.DeleteAsync(7);
        Assert.True(_backend.Sent(Method.Delete, "/ThirdParties/7"));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.CreateAsync(null!));
    }

    [Fact]
    public async Task TestTheLinksAndDeclarationsSendTheirBodies()
    {
        _backend.OnPut("/ThirdParties/7/Links/20", Party());
        _backend.OnStatus(Method.Delete, "/ThirdParties/7/Links/20", HttpStatusCode.NoContent);
        _backend.OnPut("/ThirdParties/7/Subprocessors", Party());
        _backend.OnPut("/ThirdParties/7/DataLocations", Party());

        await _service.LinkAsync(7, 20);
        Assert.True(_backend.Sent(Method.Put, "/ThirdParties/7/Links/20"));
        await _service.LinkAsync(7, 20, new ThirdPartyLinkRequest { Description = "Hosts the portal" });
        Assert.True(BodyHas("Hosts the portal"));

        await _service.UnlinkAsync(7, 20);
        Assert.True(_backend.Sent(Method.Delete, "/ThirdParties/7/Links/20"));

        await _service.SetSubprocessorsAsync(7, new ThirdPartySubprocessorsRequest
            { Subprocessors = [new ThirdPartySubprocessorRequest { Name = "Hyperscaler", Country = "US" }] });
        Assert.True(BodyHas("Hyperscaler"));

        await _service.SetDataLocationsAsync(7, new ThirdPartyDataLocationsRequest
            { Locations = [new ThirdPartyDataLocationRequest { Country = "BR", Region = "sa-east-1", Purpose = ThirdPartyDataLocationPurpose.Storage }] });
        Assert.True(BodyHas("sa-east-1"));
    }

    [Fact]
    public async Task TestTheHecvatAndSbomWritesSendTheirBodies()
    {
        _backend.OnPost("/ThirdParties/7/Assessments", Assessment());
        _backend.OnPut("/ThirdParties/7/Assessments/8/Answers", Assessment());
        _backend.OnPost("/ThirdParties/7/Assessments/8/Void", Assessment());
        _backend.OnPost("/ThirdParties/7/Sboms", Sbom());
        _backend.OnStatus(Method.Delete, "/ThirdParties/7/Sboms/9", HttpStatusCode.NoContent);

        await _service.RecordAssessmentAsync(7, new ThirdPartyAssessmentRequest { FrameworkVersion = "3.06", ExpectedQuestionCount = 321 });
        Assert.True(BodyHas("321"));

        await _service.ReplaceAnswersAsync(7, 8, new ThirdPartyAssessmentAnswersRequest
            { Answers = [new HecvatAnswerRequest { QuestionId = "HFIH-07", Answer = HecvatAnswer.No }] });
        Assert.True(BodyHas("HFIH-07"));

        await _service.VoidAssessmentAsync(7, 8, new ThirdPartyAssessmentVoidRequest { Reason = "Wrong vendor's workbook." });
        Assert.True(BodyHas("Wrong vendor"));

        var sbom = await _service.ImportSbomAsync(7, new ThirdPartySbomRequest { ComponentName = "Moodle", Document = "{\"spdxVersion\":\"SPDX-2.3\"}" });
        Assert.Equal(SbomFormat.SpdxJson, sbom.Format);
        Assert.True(BodyHas("spdxVersion"));

        await _service.DeleteSbomAsync(7, 9);
        Assert.True(_backend.Sent(Method.Delete, "/ThirdParties/7/Sboms/9"));
    }

    // --- errors -----------------------------------------------------------------------------

    private static (string Name, Method Method, string Path, Func<IThirdPartiesService, Task> Call)[] Calls() =>
    [
        ("GetThirdParties", Method.Get, "/ThirdParties", s => s.GetThirdPartiesAsync()),
        ("GetThirdParty", Method.Get, "/ThirdParties/7", s => s.GetThirdPartyAsync(7)),
        ("GetConcentration", Method.Get, "/ThirdParties/Concentration", s => s.GetConcentrationAsync()),
        ("GetByEntity", Method.Get, "/ThirdParties/ByEntity/50", s => s.GetByEntityAsync(50)),
        ("GetAssessment", Method.Get, "/ThirdParties/7/Assessments/8", s => s.GetAssessmentAsync(7, 8)),
        ("GetSbom", Method.Get, "/ThirdParties/7/Sboms/9", s => s.GetSbomAsync(7, 9)),
        ("GetHistory", Method.Get, "/ThirdParties/7/History", s => s.GetHistoryAsync(7)),
        ("Create", Method.Post, "/ThirdParties", s => s.CreateAsync(new ThirdPartyRequest())),
        ("Update", Method.Put, "/ThirdParties/7", s => s.UpdateAsync(7, new ThirdPartyRequest())),
        ("Delete", Method.Delete, "/ThirdParties/7", s => s.DeleteAsync(7)),
        ("Link", Method.Put, "/ThirdParties/7/Links/20", s => s.LinkAsync(7, 20)),
        ("Unlink", Method.Delete, "/ThirdParties/7/Links/20", s => s.UnlinkAsync(7, 20)),
        ("SetSubprocessors", Method.Put, "/ThirdParties/7/Subprocessors", s => s.SetSubprocessorsAsync(7, new ThirdPartySubprocessorsRequest())),
        ("SetDataLocations", Method.Put, "/ThirdParties/7/DataLocations", s => s.SetDataLocationsAsync(7, new ThirdPartyDataLocationsRequest())),
        ("RecordAssessment", Method.Post, "/ThirdParties/7/Assessments", s => s.RecordAssessmentAsync(7, new ThirdPartyAssessmentRequest())),
        ("ReplaceAnswers", Method.Put, "/ThirdParties/7/Assessments/8/Answers", s => s.ReplaceAnswersAsync(7, 8, new ThirdPartyAssessmentAnswersRequest())),
        ("VoidAssessment", Method.Post, "/ThirdParties/7/Assessments/8/Void", s => s.VoidAssessmentAsync(7, 8, new ThirdPartyAssessmentVoidRequest())),
        ("ImportSbom", Method.Post, "/ThirdParties/7/Sboms", s => s.ImportSbomAsync(7, new ThirdPartySbomRequest())),
        ("DeleteSbom", Method.Delete, "/ThirdParties/7/Sboms/9", s => s.DeleteSbomAsync(7, 9))
    ];

    [Fact]
    public void TestEveryClientMethodIsCoveredByTheRefusalChecks()
    {
        var methods = typeof(IThirdPartiesService).GetMethods().Select(m => m.Name).Distinct().Count();
        Assert.Equal(19, methods);
        Assert.Equal(methods, Calls().Length);
    }

    [Fact]
    public async Task TestA404IsNotFoundOnEveryMethod()
    {
        foreach (var (_, method, path, _) in Calls()) _backend.OnStatus(method, path, HttpStatusCode.NotFound);

        foreach (var (_, _, _, call) in Calls())
            await Assert.ThrowsAsync<DataNotFoundException>(() => call(_service));
    }

    /// <summary>A supplier in use, a duplicate, a validation error, a refused scope and a body too large keep the server's sentence.</summary>
    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, "The supplier is in use: end the relationship instead.")]
    [InlineData(HttpStatusCode.Conflict, "A third party with this name is already registered.")]
    [InlineData(HttpStatusCode.BadRequest, "The SBOM document is not valid JSON.")]
    [InlineData(HttpStatusCode.Forbidden, "Outside your entities.")]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "Request body too large.")]
    public async Task TestARefusalKeepsTheServersSentenceOnEveryMethod(HttpStatusCode status, string sentence)
    {
        foreach (var (_, method, path, _) in Calls())
            _backend.On(method, path, new { error = "refused", message = sentence }, status);

        foreach (var (name, _, _, call) in Calls())
        {
            var thrown = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => call(_service));
            Assert.True(thrown.Message.Contains(sentence), name);
        }
    }

    [Fact]
    public async Task TestAServerErrorIsAGenericFailureOnEveryMethod()
    {
        foreach (var (_, method, path, _) in Calls()) _backend.OnStatus(method, path, HttpStatusCode.InternalServerError);

        foreach (var (name, _, path, call) in Calls())
        {
            var thrown = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => call(_service));
            Assert.True(thrown.Message.Contains($"Error calling {path}"), name);
        }
    }

    [Fact]
    public async Task TestAnEmptySuccessBodyIsAFailureNotANull()
    {
        _backend.OnStatus(Method.Get, "/ThirdParties/7", HttpStatusCode.OK);

        var thrown = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetThirdPartyAsync(7));
        Assert.Contains("empty body", thrown.Message);
    }

    [Fact]
    public async Task TestAnUnreachableServerIsACommunicationFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/ThirdParties/Concentration");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetConcentrationAsync());
    }
}
