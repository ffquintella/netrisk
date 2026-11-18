using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.Mock;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.Integrations;
using ServerServices.Integrations.EntityMappings;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// M55, T263 (S40 §8.6, §12.3 <c>EntityMappingsControllerTest</c>): each action's success shape — 201 with the location on
/// create, 204 on delete — and every refusal of the rules' service as <c>{ error, message }</c> with its status.
/// </summary>
[TestSubject(typeof(EntityMappingsController))]
public class EntityMappingsControllerTest : BaseControllerTest
{
    private readonly EntityMappingsController _controller;

    public EntityMappingsControllerTest()
    {
        _controller = _serviceProvider.GetRequiredService<EntityMappingsController>();
        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
    }

    private static VendorEntityMappingUpsert Body(string value = "BU: Law") =>
        new((int)IntegrationKind.TrendMicroVisionOne, 1, VendorMatchKind.V1CustomTag, value, null, 8, 1,
            RestampPolicy.OnlyUnassigned, true);

    private static (int? Status, string? Error) Refusal(IActionResult? result)
    {
        var refused = Assert.IsType<ObjectResult>(result);
        var error = JsonDocument.Parse(JsonSerializer.Serialize(refused.Value)).RootElement.GetProperty("error").GetString();
        return (refused.StatusCode, error);
    }

    [Fact]
    public async Task TestTheReadsAnswer200()
    {
        var rules = Assert.IsAssignableFrom<IReadOnlyList<VendorEntityMappingDto>>(
            Assert.IsType<OkObjectResult>((await _controller.List(2, 1)).Result).Value);
        Assert.Equal("Law School", Assert.Single(rules).EntityName);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<VendorEntityMappingDto>>(
            Assert.IsType<OkObjectResult>((await _controller.List(2, 9)).Result).Value));

        var candidates = Assert.IsAssignableFrom<IReadOnlyList<VendorMappingCandidateDto>>(
            Assert.IsType<OkObjectResult>((await _controller.Candidates(2, 1)).Result).Value);
        Assert.Equal(412, Assert.Single(candidates).ObjectsSeen);
    }

    [Fact]
    public async Task TestCreateAnswers201WithTheRule()
    {
        var created = Assert.IsType<CreatedResult>((await _controller.Create(Body())).Result);

        Assert.Equal("Integrations/EntityMappings/5", created.Location);
        Assert.Equal(5, Assert.IsType<VendorEntityMappingDto>(created.Value).Id);
    }

    [Fact]
    public async Task TestUpdateDeleteAndPreviewAnswerTheirSuccess()
    {
        Assert.Equal(1, Assert.IsType<VendorEntityMappingDto>(
            Assert.IsType<OkObjectResult>((await _controller.Update(1, Body())).Result).Value).Id);
        Assert.IsType<NoContentResult>(await _controller.Delete(1));
        var preview = Assert.IsType<VendorEntityMappingPreviewDto>(
            Assert.IsType<OkObjectResult>((await _controller.Preview(Body(), 1)).Result).Value);
        Assert.Equal((412, 37, 12, 3, 360),
            (preview.Matched, preview.WouldAssign, preview.WouldReassign, preview.Conflicts, preview.AlreadyAssigned));
    }

    [Fact]
    public async Task TestEveryRefusalIsMappedWithItsStatusAndCode()
    {
        Assert.Equal((409, VendorEntityMappingErrors.Duplicate), Refusal((await _controller.Create(Body("dup"))).Result));
        Assert.Equal((403, VendorEntityMappingErrors.RequiresGlobalScope),
            Refusal((await _controller.Update(1, Body("scoped"))).Result));
        Assert.Equal((403, VendorEntityMappingErrors.RequiresGlobalScope),
            Refusal((await _controller.Preview(Body("scoped"))).Result));
        Assert.Equal((404, VendorEntityMappingErrors.NotFound), Refusal((await _controller.Update(404, Body())).Result));
        Assert.Equal((404, VendorEntityMappingErrors.NotFound), Refusal(await _controller.Delete(404)));
        Assert.Equal((400, VendorEntityMappingErrors.ValueRequired), Refusal((await _controller.Create(Body("bad"))).Result));
    }

    [Fact]
    public async Task TestAMissingBodyIs400NotA500()
    {
        Assert.Equal((400, VendorEntityMappingErrors.ValueRequired), Refusal((await _controller.Create(null)).Result));
        Assert.Equal((400, VendorEntityMappingErrors.ValueRequired), Refusal((await _controller.Update(1, null)).Result));
        Assert.Equal((400, VendorEntityMappingErrors.ValueRequired), Refusal((await _controller.Preview(null)).Result));
    }

    [Fact]
    public async Task TestAnUnexpectedFailureIs500WithoutDetail()
    {
        var result = Assert.IsType<StatusCodeResult>((await _controller.Create(Body("boom"))).Result);

        Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
    }
}
