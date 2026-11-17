using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using ServerServices.Security;
using ILogger = Serilog.ILogger;

namespace API.Middleware;

/// <summary>
/// T358 (S55 "Audit and attribution"): one log line per authenticated request the desktop client made for an agent
/// tool, naming the user and the agent. The field-level trail (<c>audit_logs.actor</c>) carries the same annotation for
/// the audited types (<see cref="AgentAttribution.AuditActor"/>); this line covers every other endpoint. It only reads:
/// it runs after authorization decided and changes nothing about the request (<c>AgentContextHeaderTest</c>).
/// </summary>
public class AgentAttributionMiddleware(RequestDelegate next, ILogger logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        finally
        {
            if (Describe(context) is { } line) logger.Information("Agent-assisted request: {Line}", line);
        }
    }

    /// <summary>
    /// What is logged for <paramref name="context"/>: user id, login, agent id, method, path and status; null when the
    /// request is anonymous or carries no valid agent header. The path is the route only, never the query string.
    /// </summary>
    public static string? Describe(HttpContext context)
    {
        if (AgentAttribution.AgentOf(context) is not { } agent) return null;
        var user = context.User;
        var userId = user.FindFirst(ClaimTypes.Sid)?.Value ?? "?";
        var login = global::Tools.User.UserHelper.GetUserName(user.Identity!) ?? "?";
        return $"user {userId} ({login}) via agent {agent}: {context.Request.Method} {context.Request.Path.Value} " +
               $"-> {context.Response.StatusCode}";
    }
}
