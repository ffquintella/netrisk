using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using API.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Integrations;
using ServerServices.Integrations.EntityMappings;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// The vendor → entity rules (S40 §6.6, §8.6; M55, T263): which entity the hosts a Vision One connection reports with a
/// custom tag are attributed to. Under <c>configuration</c>, like every integration; the service adds the global-scope rule
/// for writes and the preview, since a rule moves who sees a host. Refusals are <c>{ error, message }</c>.
/// </summary>
[ApiController]
[PermissionAuthorize("configuration")]
[Route("Integrations/EntityMappings")]
public class EntityMappingsController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IVendorEntityMappingService service)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    private async Task<ActionResult<T>> Run<T>(string operation, Func<Task<T>> action)
    {
        GetUser();
        try
        {
            return Ok(await action());
        }
        catch (Exception ex)
        {
            return Refusal(ex, operation);
        }
    }

    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<VendorEntityMappingDto>))]
    public Task<ActionResult<IReadOnlyList<VendorEntityMappingDto>>> List([FromQuery] int provider,
        [FromQuery] int connectionId, CancellationToken ct = default) =>
        Run("listing the vendor mappings", () => service.ListAsync(provider, connectionId, ct));

    [HttpGet]
    [Route("Candidates")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<VendorMappingCandidateDto>))]
    public Task<ActionResult<IReadOnlyList<VendorMappingCandidateDto>>> Candidates([FromQuery] int provider,
        [FromQuery] int connectionId, CancellationToken ct = default) =>
        Run("listing the mapping candidates", () => service.GetCandidatesAsync(provider, connectionId, ct));

    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(VendorEntityMappingDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VendorEntityMappingDto>> Create([FromBody] VendorEntityMappingUpsert? request,
        CancellationToken ct = default)
    {
        var user = GetUser();
        try
        {
            var created = await service.CreateAsync(Required(request), user.Value, ct);
            return Created($"Integrations/EntityMappings/{created.Id}", created);
        }
        catch (Exception ex)
        {
            return Refusal(ex, "creating a vendor mapping");
        }
    }

    [HttpPut]
    [Route("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(VendorEntityMappingDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<VendorEntityMappingDto>> Update(int id, [FromBody] VendorEntityMappingUpsert? request,
        CancellationToken ct = default) =>
        Run("updating a vendor mapping", () => service.UpdateAsync(id, Required(request), ct));

    [HttpDelete]
    [Route("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(int id, CancellationToken ct = default)
    {
        GetUser();
        try
        {
            await service.DeleteAsync(id, ct);
            return NoContent();
        }
        catch (Exception ex)
        {
            return Refusal(ex, "deleting a vendor mapping");
        }
    }

    /// <summary>What an unsaved rule would do on the next sync; <paramref name="mappingId"/> names the rule being edited.</summary>
    [HttpPost]
    [Route("Preview")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(VendorEntityMappingPreviewDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<ActionResult<VendorEntityMappingPreviewDto>> Preview([FromBody] VendorEntityMappingUpsert? request,
        [FromQuery] int? mappingId = null, CancellationToken ct = default) =>
        Run("previewing a vendor mapping", () => service.PreviewAsync(Required(request), mappingId, ct));

    private static VendorEntityMappingUpsert Required(VendorEntityMappingUpsert? request) =>
        request ?? throw VendorEntityMappingException.BadRequest(VendorEntityMappingErrors.ValueRequired, "A body is required.");

    /// <summary>A refusal as <c>{ error, message }</c> with its status; anything else is logged and answered 500.</summary>
    private ActionResult Refusal(Exception exception, string operation)
    {
        switch (exception)
        {
            case DAL.Exceptions.EntityScopeViolationException:
                // The middleware answers 403 for it; swallowed here it would be a 500.
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(exception);
                return null!;
            case VendorEntityMappingException ex:
                return StatusCode(ex.Status, new { error = ex.Code, message = ex.Message });
            case OperationCanceledException:
                return StatusCode(499);
            default:
                Logger.Error(exception, "Unknown error {Operation}", operation);
                return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}
