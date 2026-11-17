using DAL.Context;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using ServerServices.Interfaces;
using ServerServices.Security;
using ILogger = Serilog.ILogger;

namespace ServerServices.Services;

/// <summary>
/// Comments on a fix request, risk, vulnerability or host. T375: <see cref="CreateUserCommentAsync"/> takes the author
/// from the authenticated <see cref="User"/> and checks the caller may read the record. An anonymous comment stores its
/// real author in <c>UserId</c>, and every read here returns it through <see cref="ForReaders"/>, which withholds that
/// id — except <see cref="GetUserCommentsAsync"/>, the author's own list.
/// </summary>
public class CommentsService : ServiceBase, ICommentsService
{
    public const string FixRequestType = "FixRequest";
    public const string RiskType = "Risk";
    public const string VulnerabilityType = "Vulnerability";
    public const string HostType = "Host";

    /// <summary>
    /// <c>comments.Text</c> is a MariaDB <c>TEXT</c> (65,535 bytes); a UTF-16 unit is at most three UTF-8 bytes,
    /// so 20,000 units always fit.
    /// </summary>
    public const int MaxTextLength = 20_000;

    /// <summary><c>comments.CommenterName</c> is <c>varchar(255)</c> (<c>DB/Structure/28.sql</c>).</summary>
    public const int MaxCommenterNameLength = 255;

    /// <summary>The permission behind <c>GET /Risks/{id}</c> (the <c>RequireRiskmanagement</c> policy).</summary>
    public const string RiskPermission = "riskmanagement";

    /// <summary>The permission behind <c>GET /Vulnerabilities/{id}</c> and the fix-request listing.</summary>
    public const string VulnerabilityPermission = "vulnerabilities";

    private IMessagesService MessagesService { get; }
    private IPermissionsService Permissions { get; }

    public CommentsService(ILogger logger, IDalService dalService, IMessagesService messagesService,
        IPermissionsService permissionsService) : base(logger, dalService)
    {
        MessagesService = messagesService;
        Permissions = permissionsService;
    }

    public async Task<List<Comment>> GetCommentsAsync(string type)
    {
        await using var dbContext = DalService.GetContext();

        var comments = await dbContext.Comments.Where(c => c.Type == type).ToListAsync();

        return comments.Select(ForReaders).ToList();
    }

    public async Task<List<Comment>> GetFixRequestCommentsAsync(int fixRequestId)
    {
        await using var dbContext = DalService.GetContext();

        var comments = await dbContext.Comments.Where(c => c.Type == "FixRequest" && c.FixRequestId == fixRequestId).ToListAsync();

        return comments.Select(ForReaders).ToList();
    }

    public async Task<List<Comment>> GetFixRequestCommentsForUserAsync(User reader, int fixRequestId)
    {
        ArgumentNullException.ThrowIfNull(reader);

        await using var dbContext = DalService.GetContext();

        // Missing, out of scope and not taking part are the same answer, so the route is no existence oracle.
        var fixRequest = await dbContext.FixRequests.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fixRequestId);
        if (fixRequest == null || !await IsFixRequestParticipantAsync(dbContext, reader, fixRequest))
        {
            Logger.Warning("Refused the comments of fix request {FixRequest} to user {User}: not visible to them",
                fixRequestId, reader.Value);
            throw new DataNotFoundException(FixRequestType, fixRequestId.ToString());
        }

        var comments = await dbContext.Comments
            .Where(c => c.Type == FixRequestType && c.FixRequestId == fixRequestId).ToListAsync();

        return comments.Select(ForReaders).ToList();
    }

    public async Task<List<Comment>> GetHostCommentsAsync(int hostId)
    {
        await using var dbContext = DalService.GetContext();

        // Through the scoped context: another entity's host is not found, so neither are its comments.
        if (!await dbContext.Hosts.AnyAsync(h => h.Id == hostId))
            throw new DataNotFoundException(HostType, hostId.ToString());

        var comments = await dbContext.Comments
            .Where(c => c.Type == "Host" && c.HostId == hostId)
            .OrderBy(c => c.Date).ThenBy(c => c.Id)
            .ToListAsync();

        return comments.Select(ForReaders).ToList();
    }

    /// <summary>The comments <paramref name="userId"/> wrote: the author's own list, so their id stays on them.</summary>
    public async Task<List<Comment>> GetUserCommentsAsync(int userId)
    {
        await using var dbContext = DalService.GetContext();

        var comments = await dbContext.Comments.Where(c => c.UserId == userId).ToListAsync();

        return comments;
    }

    /// <summary>
    /// A comment as a reader other than its author sees it: no navigations, and no author id when it is anonymous.
    /// <c>CommenterName</c> is kept: an anonymous comment from the API stores none, and one from the public fix report
    /// carries the address the external fixer typed, which the requester needs.
    /// </summary>
    public static Comment ForReaders(Comment comment)
    {
        var copy = Detached(comment);
        if (copy.IsAnonymous) copy.UserId = null;
        return copy;
    }

    public async Task<Comment> CreateUserCommentAsync(
        User author,
        string? type,
        int? replyTo,
        bool isAnonymous,
        string? text,
        int? fixRequestId,
        int? riskId,
        int? vulnerabilityId,
        int? hostId)
    {
        ArgumentNullException.ThrowIfNull(author);

        var target = ResolveTarget(type, fixRequestId, riskId, vulnerabilityId, hostId);
        ValidateText(text);

        await using var dbContext = DalService.GetContext();

        var fixRequest = await AuthorizeAsync(dbContext, author, target);

        // After the authorization, so a caller who may not see the record cannot probe its comment ids.
        await EnsureReplyIsOnTargetAsync(dbContext, replyTo, target);

        // An unclaimed fix request adopts its commenter: the authenticated author, never an id from the request.
        if (fixRequest is { RequestingUserId: null })
            fixRequest.RequestingUserId = author.Value;

        var comment = NewComment(target, author.Value, DateTime.Now, replyTo, isAnonymous,
            isAnonymous ? null : author.Name, text!);

        dbContext.Comments.Add(comment);
        await dbContext.SaveChangesAsync();

        await NotifyRequesterAsync(fixRequest);

        return Detached(comment);
    }

    public async Task<Comment> CreateCommentsAsync(
        int? userId,
        DateTime date,
        int? replyTo,
        string type,
        bool isAnonymous,
        string commenterName,
        string text,
        int? fixRequestId,
        int? riskId,
        int? vulnerabilityId,
        int? hostId
        )
    {
        var target = ResolveTarget(type, fixRequestId, riskId, vulnerabilityId, hostId);
        ValidateText(text);
        ValidateCommenterName(commenterName);

        await using var dbContext = DalService.GetContext();

        var fixRequest = await FindTargetAsync(dbContext, target);

        await EnsureReplyIsOnTargetAsync(dbContext, replyTo, target);

        // No requester adoption: userId is what the website ingest reports (the fixer picks a name from a list), not
        // an authenticated identity (T375).
        var comment = NewComment(target, userId, date, replyTo, isAnonymous, commenterName, text);

        dbContext.Comments.Add(comment);
        await dbContext.SaveChangesAsync();

        await NotifyRequesterAsync(fixRequest);

        return Detached(comment);
    }

    // --- target --------------------------------------------------------------------------------------------------

    /// <summary>The one record a comment is about, named by its type.</summary>
    private readonly record struct Target(string Type, int Id);

    /// <summary>
    /// The record the type names. Ids of other kinds are dropped, not stored: an id nobody authorized would hang the
    /// comment off an unchecked record. (The desktop fix chat sends the vulnerability id with the fix request.)
    /// </summary>
    private static Target ResolveTarget(string? type, int? fixRequestId, int? riskId, int? vulnerabilityId, int? hostId)
    {
        var (id, parameter) = type switch
        {
            FixRequestType => (fixRequestId, "fixRequestId"),
            RiskType => (riskId, "riskId"),
            VulnerabilityType => (vulnerabilityId, "vulnerabilityId"),
            HostType => (hostId, "hostId"),
            _ => throw new InvalidParameterException("type",
                "A comment's type is FixRequest, Risk, Vulnerability or Host.")
        };

        if (id is not > 0)
            throw new InvalidParameterException(parameter, $"A {type} comment names the {type} it is about.");

        return new Target(type!, id.Value);
    }

    private static Comment NewComment(Target target, int? userId, DateTime date, int? replyTo, bool isAnonymous,
        string? commenterName, string text) => new()
    {
        UserId = userId,
        Date = date,
        ReplyTo = replyTo,
        Type = target.Type,
        IsAnonymous = isAnonymous,
        CommenterName = commenterName,
        Text = text,
        FixRequestId = target.Type == FixRequestType ? target.Id : null,
        RiskId = target.Type == RiskType ? target.Id : null,
        VulnerabilityId = target.Type == VulnerabilityType ? target.Id : null,
        HostId = target.Type == HostType ? target.Id : null
    };

    /// <summary>The saved row without the navigations EF fixed up on it (the tracked fix request and its report key).</summary>
    private static Comment Detached(Comment comment) => new()
    {
        Id = comment.Id,
        UserId = comment.UserId,
        Date = comment.Date,
        ReplyTo = comment.ReplyTo,
        Type = comment.Type,
        IsAnonymous = comment.IsAnonymous,
        CommenterName = comment.CommenterName,
        Text = comment.Text,
        FixRequestId = comment.FixRequestId,
        RiskId = comment.RiskId,
        VulnerabilityId = comment.VulnerabilityId,
        HostId = comment.HostId
    };

    /// <summary>
    /// Whether the record exists as the caller sees it: risks, vulnerabilities and hosts carry the entity-scope filter,
    /// and a fix request follows its vulnerability. Missing and out of scope are the same answer. A fix request comes
    /// back tracked, because commenting may adopt a requester.
    /// </summary>
    private static async Task<FixRequest?> FindTargetAsync(AuditableContext dbContext, Target target)
    {
        if (target.Type == FixRequestType)
            return await dbContext.FixRequests.FirstOrDefaultAsync(f => f.Id == target.Id)
                   ?? throw new DataNotFoundException(target.Type, target.Id.ToString());

        var visible = target.Type switch
        {
            RiskType => await dbContext.Risks.AnyAsync(r => r.Id == target.Id),
            VulnerabilityType => await dbContext.Vulnerabilities.AnyAsync(v => v.Id == target.Id),
            _ => await dbContext.Hosts.AnyAsync(h => h.Id == target.Id)
        };

        if (!visible) throw new DataNotFoundException(target.Type, target.Id.ToString());

        return null;
    }

    // --- authorization -------------------------------------------------------------------------------------------

    /// <summary>
    /// The record's read rule: Risk needs <c>riskmanagement</c>, Vulnerability <c>vulnerabilities</c>, Host
    /// <c>hosts</c> (<see cref="HostAccess"/>); a fix request admits its requester, its team, the vulnerability's
    /// analyst and <c>vulnerabilities</c>. Administrators hold every permission, nobody escapes the entity scope, and
    /// the permission is checked before the lookup so a refused caller learns nothing about which ids exist.
    /// </summary>
    private async Task<FixRequest?> AuthorizeAsync(AuditableContext dbContext, User author, Target target)
    {
        if (target.Type == FixRequestType)
        {
            var fixRequest = await FindTargetAsync(dbContext, target);
            if (await IsFixRequestParticipantAsync(dbContext, author, fixRequest!)) return fixRequest;

            Logger.Warning("Refused a comment on fix request {FixRequest} to user {User}: not its requester, team or "
                           + "analyst, and without the {Permission} permission",
                target.Id, author.Value, VulnerabilityPermission);

            // The same answer as a missing fix request, so the refusal confirms nothing about which ids exist.
            throw new DataNotFoundException(target.Type, target.Id.ToString());
        }

        var permission = target.Type switch
        {
            RiskType => RiskPermission,
            VulnerabilityType => VulnerabilityPermission,
            _ => HostAccess.Permission
        };

        if (!await HoldsAsync(author, permission))
        {
            Logger.Warning("Refused a comment on {Type} {Id} to user {User}: {Permission} required",
                target.Type, target.Id, author.Value, permission);
            throw new UserNotAuthorizedException(author.Name, author.Value, permission);
        }

        return await FindTargetAsync(dbContext, target);
    }

    /// <summary>Who may read and write a fix request's comments.</summary>
    private async Task<bool> IsFixRequestParticipantAsync(AuditableContext dbContext, User author, FixRequest fixRequest)
    {
        if (fixRequest.RequestingUserId == author.Value) return true;

        if (await HoldsAsync(author, VulnerabilityPermission)) return true;

        if (fixRequest.FixTeamId is { } teamId &&
            await dbContext.Users.AnyAsync(u => u.Value == author.Value && u.Teams.Any(t => t.Value == teamId)))
            return true;

        return await dbContext.Vulnerabilities.AnyAsync(v => v.Id == fixRequest.VulnerabilityId &&
                                                            v.AnalystId == author.Value);
    }

    private async Task<bool> HoldsAsync(User author, string permission)
    {
        if (author.Admin) return true;

        var held = await Permissions.GetUserPermissionsAsync(author);
        return held.Any(p => string.Equals(p, permission, StringComparison.OrdinalIgnoreCase));
    }

    // --- validation ----------------------------------------------------------------------------------------------

    /// <summary>A reply answers a comment on the same record; missing and elsewhere are the same refusal.</summary>
    private static async Task EnsureReplyIsOnTargetAsync(AuditableContext dbContext, int? replyTo, Target target)
    {
        if (replyTo is null) return;

        var parent = await dbContext.Comments.AsNoTracking()
            .Where(c => c.Id == replyTo.Value)
            .Select(c => new { c.Type, c.FixRequestId, c.RiskId, c.VulnerabilityId, c.HostId })
            .FirstOrDefaultAsync();

        var sameRecord = parent != null && parent.Type == target.Type && target.Type switch
        {
            FixRequestType => parent.FixRequestId == target.Id,
            RiskType => parent.RiskId == target.Id,
            VulnerabilityType => parent.VulnerabilityId == target.Id,
            _ => parent.HostId == target.Id
        };

        if (!sameRecord)
            throw new InvalidParameterException("replyTo", "A reply answers a comment on the same record.");
    }

    /// <summary>Required, bounded by the column, no control characters but line breaks and tabs.</summary>
    private static void ValidateText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidParameterException("text", "A comment needs text.");

        if (text.Length > MaxTextLength)
            throw new InvalidParameterException("text", $"A comment holds at most {MaxTextLength} characters.");

        if (text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
            throw new InvalidParameterException("text", "A comment cannot carry control characters.");
    }

    private static void ValidateCommenterName(string? commenterName)
    {
        if (commenterName is null) return;

        if (commenterName.Length > MaxCommenterNameLength)
            throw new InvalidParameterException("commenterName",
                $"A commenter name holds at most {MaxCommenterNameLength} characters.");

        if (commenterName.Any(char.IsControl))
            throw new InvalidParameterException("commenterName", "A commenter name cannot carry control characters.");
    }

    // --- notification --------------------------------------------------------------------------------------------

    /// <summary>
    /// Tells the stored requester — never an id from the request — about the new comment. Sent after the save; a
    /// failure is logged, not thrown, because the website sync retries a throwing action and would post the comment twice.
    /// </summary>
    private async Task NotifyRequesterAsync(FixRequest? fixRequest)
    {
        if (fixRequest?.RequestingUserId is not { } requester) return;

        try
        {
            await MessagesService.SendMessageAsync(
                "Your fix request #: " + fixRequest.Id + " for vulnerability " + fixRequest.VulnerabilityId +
                "  has a new comment", requester, 1, 1);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not notify user {User} of a new comment on fix request {FixRequest}",
                requester, fixRequest.Id);
        }
    }
}
