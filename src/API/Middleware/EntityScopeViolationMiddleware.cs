using System.Text.Json;
using System.Threading.Tasks;
using DAL.Exceptions;
using Microsoft.AspNetCore.Http;
using ILogger = Serilog.ILogger;

namespace API.Middleware;

/// <summary>
/// Turns a write that crossed a business-entity boundary (Track 2 milestone 2.3.1) into a 403
/// rather than letting it surface as an unhandled 500.
///
/// The guard that raises it lives in <c>AuditableContext.SaveChanges</c>, so it can fire from any
/// controller. Handling it once here means no controller has to remember to catch it — the same
/// reasoning as the model-level query filter it complements.
/// </summary>
public class EntityScopeViolationMiddleware(RequestDelegate next, ILogger logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (EntityScopeViolationException ex)
        {
            logger.Warning(
                "Refused a cross-entity write of {EntityType} into entity {EntityId}; caller scope {Scope}",
                ex.EntityType, ex.EntityId, ex.Scope);

            if (context.Response.HasStarted)
            {
                // Too late to change the status line; let it surface rather than corrupting the body.
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";

            // The message names the entity ids involved, which the caller already supplied, so it
            // leaks nothing they did not send.
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "entity_scope_violation",
                message = ex.Message
            }));
        }
    }
}
