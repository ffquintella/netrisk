using System;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ServerServices.RiskIndex.Read;
using ILogger = Serilog.ILogger;

namespace API.Controllers.RiskIndex;

/// <summary>
/// The index API's refusals onto HTTP (S40 §8.1): <c>{ error, message, details? }</c> with the stable code the desktop
/// client translates, <c>Retry-After</c> on the 503 of the derived-computation budget. Anything else is logged and a bare
/// 500 — never the exception's text (NRM §5.3.5: errors are logged, not shown).
/// </summary>
internal static class RiskIndexApiErrors
{
    public static ActionResult Map(ControllerBase controller, ILogger logger, Exception exception, string operation)
    {
        switch (exception)
        {
            case DAL.Exceptions.EntityScopeViolationException:
                // The middleware answers 403 for it; swallowed here it would be a 500.
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(exception);
                return null!;
            case RiskIndexException ex:
                if (ex.RetryAfter is { } retry && controller.HttpContext is { } http)
                    http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retry.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                if (ex.Status >= 500)
                    logger.Warning("Risk index {Operation} refused: {Code}", operation, ex.Code);
                return controller.StatusCode(ex.Status, ex.Details is null
                    ? new { error = ex.Code, message = ex.Message }
                    : new { error = ex.Code, message = ex.Message, details = (object?)ex.Details });
            case OperationCanceledException:
                return controller.StatusCode(499);
            default:
                logger.Error(exception, "Unknown error {Operation}", operation);
                return controller.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}
