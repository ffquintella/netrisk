using DAL.Context;
using Microsoft.AspNetCore.Http;
using Model.Agents;
using Tools.User;

namespace ServerServices.Security;

/// <summary>
/// "User U via agent A" (T358, S55 "Audit and attribution"): what the API records for a request the desktop client made
/// on behalf of an agent tool. It reads <see cref="AgentContextHeader"/> only for an authenticated request and only to
/// annotate that user's own records; identity, scope and authorization never read it (<c>AgentContextHeaderTest</c>).
/// </summary>
public static class AgentAttribution
{
    /// <summary>The agent registration id of an authenticated request, or null (no header, invalid, or anonymous).</summary>
    public static string? AgentOf(HttpContext? http)
    {
        if (http?.User.Identity is not { IsAuthenticated: true }) return null;
        return AgentContextHeader.Parse(http.Request.Headers[AgentContextHeader.Name].ToString());
    }

    /// <summary>
    /// The <c>audit_logs.actor</c> of a save: the authenticated login, plus <c>" via agent &lt;id&gt;"</c> when the
    /// request came through an agent tool; the system actor without an authenticated principal (the header is ignored).
    /// </summary>
    public static string AuditActor(HttpContext? http)
    {
        var identity = http?.User.Identity;
        if (identity is not { IsAuthenticated: true }) return AuditableContext.SystemActor;
        var name = UserHelper.GetUserName(identity);
        if (string.IsNullOrWhiteSpace(name)) return AuditableContext.SystemActor;
        return AgentContextHeader.Actor(name, AgentOf(http));
    }
}
