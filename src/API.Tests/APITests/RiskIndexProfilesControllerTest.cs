using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using API.Controllers;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.RiskIndex;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// M53, T250 (S40 §8.4, §12.3 <c>RiskIndexProfilesControllerTest</c>): each lifecycle action's success shape, the
/// <c>If-Match</c> contract of the draft's edit (428 without it), and the refusals of the service with their codes.
/// </summary>
[TestSubject(typeof(RiskIndexProfilesController))]
public class RiskIndexProfilesControllerTest : BaseControllerTest
{
    private readonly RiskIndexProfilesController _controller;
    private readonly HttpContext _context;

    private static readonly RiskIndexProfileUpdateRequest Update =
        new("Pilot", RiskIndexMode.Interim, new RiskIndexParameters());

    public RiskIndexProfilesControllerTest()
    {
        _controller = _serviceProvider.GetRequiredService<RiskIndexProfilesController>();
        _context = _serviceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext!;
    }

    private static string? Error(IActionResult? result) =>
        JsonDocument.Parse(JsonSerializer.Serialize(((ObjectResult)result!).Value)).RootElement.GetProperty("error").GetString();

    [Fact]
    public async Task TestTheReadsAnswer200Or204()
    {
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<RiskIndexProfileSummaryDto>>(
            Assert.IsType<OkObjectResult>((await _controller.List()).Result).Value));
        Assert.Equal(1, Assert.IsType<RiskIndexProfileDto>(Assert.IsType<OkObjectResult>((await _controller.Get(1)).Result).Value).Id);
        Assert.IsType<NoContentResult>((await _controller.GetActive()).Result);
        Assert.IsType<RiskIndexProfileDiffDto>(Assert.IsType<OkObjectResult>((await _controller.Diff(1, 2)).Result).Value);
        Assert.NotEmpty(Assert.IsType<RiskIndexParameterSchemaDto>(Assert.IsType<OkObjectResult>((await _controller.Schema()).Result).Value).Items);
        Assert.NotEmpty(Assert.IsAssignableFrom<IReadOnlyList<RiskIndexPresetDto>>(
            Assert.IsType<OkObjectResult>((await _controller.Presets()).Result).Value));
        Assert.Equal(0.97, Assert.IsType<RiskIndexPreviewDto>(Assert.IsType<OkObjectResult>((await _controller.GetPreview(1)).Result).Value)
            .KendallTau);
        Assert.IsType<RiskIndexBridgeDto>(Assert.IsType<OkObjectResult>((await _controller.Bridge(1)).Result).Value);
        Assert.IsAssignableFrom<IReadOnlyList<AuditLog>>(Assert.IsType<OkObjectResult>((await _controller.History(1)).Result).Value);
    }

    [Fact]
    public async Task TestTheLifecycleActions()
    {
        var created = Assert.IsType<CreatedAtActionResult>((await _controller.Create(new RiskIndexProfileCreateRequest("v1", null, "balanced"))).Result);
        Assert.Equal(nameof(RiskIndexProfilesController.Get), created.ActionName);
        Assert.IsType<NoContentResult>(await _controller.Delete(1));
        Assert.IsType<RiskIndexValidationResultDto>(Assert.IsType<OkObjectResult>((await _controller.Validate(1)).Result).Value);
        Assert.Equal(StatusCodes.Status202Accepted, Assert.IsType<AcceptedResult>((await _controller.RequestPreview(1)).Result).StatusCode);
        Assert.Equal(RiskIndexProfileStatus.PendingApproval, Assert.IsType<RiskIndexProfileDto>(
            Assert.IsType<OkObjectResult>((await _controller.Submit(1, new RiskIndexSubmitRequest("why"))).Result).Value).Status);
        Assert.Equal("role:102", Assert.IsType<RiskIndexApprovalEligibilityDto>(
            Assert.IsType<OkObjectResult>((await _controller.ApprovalEligibility(1)).Result).Value).GrantSource);
        Assert.Equal(RiskIndexProfileStatus.Active, Assert.IsType<RiskIndexProfileDto>(Assert.IsType<OkObjectResult>(
            (await _controller.Approve(1, new RiskIndexApproveRequest("CRTI", "Board", false, null))).Result).Value).Status);
        Assert.Equal(RiskIndexProfileStatus.Rejected, Assert.IsType<RiskIndexProfileDto>(Assert.IsType<OkObjectResult>(
            (await _controller.Reject(1, new RiskIndexRejectRequest("no"))).Result).Value).Status);
        Assert.Equal(RiskIndexProfileStatus.Retired, Assert.IsType<RiskIndexProfileDto>(Assert.IsType<OkObjectResult>(
            (await _controller.Retire(1, new RiskIndexRetireRequest("defect", "CRTI"))).Result).Value).Status);
    }

    /// <summary>S40 §8.4: linking the committee's minutes answers 204; listing them is the profile's evidence.</summary>
    [Fact]
    public async Task TestTheCommitteeEvidenceIsLinkedAndListed()
    {
        Assert.IsType<NoContentResult>(await _controller.Attach(1, new RiskIndexAttachRequest(41)));
        Assert.Equal("ata-crti.pdf", Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<Model.DTO.FileListing>>(
            Assert.IsType<OkObjectResult>((await _controller.Attachments(1)).Result).Value)).Name);
    }

    /// <summary>S40 §8.4: the edit needs <c>If-Match</c> (428 without it); quoted and weak forms both carry the revision.</summary>
    [Theory]
    [InlineData("3", 4)]
    [InlineData("\"3\"", 4)]
    [InlineData("W/\"7\"", 8)]
    public async Task TestTheEditReadsTheRevisionFromIfMatch(string header, int expected)
    {
        _context.Request.Headers.IfMatch = header;

        var result = await _controller.Update(1, Update);

        Assert.Equal(expected, Assert.IsType<RiskIndexProfileDto>(Assert.IsType<OkObjectResult>(result.Result).Value).Revision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public async Task TestAnEditWithoutAUsableIfMatchIs428(string? header)
    {
        _context.Request.Headers.IfMatch = header;

        var result = (await _controller.Update(1, Update)).Result;

        Assert.Equal(StatusCodes.Status428PreconditionRequired, ((ObjectResult)result!).StatusCode);
        Assert.Equal("if_match_required", Error(result));
    }

    [Theory]
    [InlineData(400, "committee_ref_required")]
    [InlineData(403, "sod_author_cannot_approve")]
    [InlineData(404, "profile_not_found")]
    [InlineData(409, "stale_draft")]
    public async Task TestEachRefusalKeepsItsStatusAndCode(int id, string code)
    {
        _context.Request.Headers.IfMatch = "1";
        var results = new IActionResult?[]
        {
            (await _controller.Get(id)).Result, (await _controller.Update(id, Update)).Result, await _controller.Delete(id),
            (await _controller.Validate(id)).Result, (await _controller.RequestPreview(id)).Result, (await _controller.GetPreview(id)).Result,
            (await _controller.Submit(id, new RiskIndexSubmitRequest("x"))).Result, (await _controller.ApprovalEligibility(id)).Result,
            (await _controller.Approve(id, null)).Result, (await _controller.Reject(id, null)).Result,
            (await _controller.Retire(id, null)).Result, (await _controller.Bridge(id)).Result, (await _controller.History(id)).Result,
            await _controller.Attach(id, null), (await _controller.Attachments(id)).Result
        };

        Assert.All(results, r =>
        {
            Assert.Equal(id, ((ObjectResult)r!).StatusCode);
            Assert.Equal(code, Error(r));
        });
        Assert.Equal("open_draft_exists", Error((await _controller.Create(new RiskIndexProfileCreateRequest("dup", null, null))).Result));
    }
}
