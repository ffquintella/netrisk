using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// The domain exceptions of Stage 9.9 (S50 §6) onto the status codes the other controllers use for them, shared by the
/// archive, backtesting and committee controllers so the three cannot answer the same refusal differently — and by the
/// Stage 9.10 third-party register (S51 §6), the Stage 9.11 LGPD data catalogue (S52 §6) and the Stage 9.12 AI model
/// inventory (S53 §6), whose refusals are the same exceptions.
/// </summary>
internal static class DecisionCycleErrors
{
    public static ActionResult Map(ControllerBase controller, ILogger logger, Exception exception, string operation)
    {
        switch (exception)
        {
            case DAL.Exceptions.EntityScopeViolationException:
                // Swallowed here it would be a 500; the middleware answers 403 (S42 §6 precedent).
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(exception);
                return null!;
            case InvalidParameterException ex:
                return controller.BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
            case DataNotFoundException:
                return controller.NotFound();
            case DataAlreadyExistsException ex:
                return controller.Conflict(new { error = "already_exists", ex.Identification, ex.Message });
            case PermissionInvalidException ex:
                return controller.StatusCode(StatusCodes.Status403Forbidden,
                    new { error = "not_permitted", ex.Permission, message = ex.Message });
            case RuleBrokenException ex:
                return controller.UnprocessableEntity(new { error = ex.RuleName, ex.Message });
            case InvalidStateTransitionException ex:
                return controller.UnprocessableEntity(new
                    { error = "invalid_transition", ex.FromState, ex.ToState, ex.Message });
            default:
                logger.Error(exception, "Unknown error {Operation}", operation);
                return controller.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}
