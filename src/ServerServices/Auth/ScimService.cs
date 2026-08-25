using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DAL.Context;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Authentication.Federation;
using Model.Authentication.Scim;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace ServerServices.Auth;

/// <summary>
/// SCIM 2.0 user and group provisioning (Track 4 milestone 4.3.2).
///
/// Two design decisions are worth stating up front. A SCIM group is a NetRisk *role*, because that is
/// the only group-shaped thing NetRisk has and inventing a parallel group table would leave two
/// notions of membership to keep in sync. And deactivation sets <c>Enabled = false</c> and
/// <c>Lockout = 1</c>, which takes effect on the next request rather than at the next token expiry:
/// every authenticated request re-reads the user and requires both, so an IdP deprovision revokes
/// live sessions in the sense that matters.
/// </summary>
public class ScimService(ILogger logger, IDalService dalService) : ServiceBase(logger, dalService), IScimService
{
    /// <summary>Maximum page size, whatever the caller asks for. Entra ID defaults to 100.</summary>
    private const int MaxPageSize = 200;

    private const int SecretBytes = 32;

    private const int KeyIdBytes = 8;

    /// <summary>
    /// <c>attribute eq "value"</c> — the only filter shape an IdP sends for provisioning, and the only
    /// one supported. Parsing a general SCIM filter grammar to support filters nobody sends would be
    /// surface for no benefit.
    /// </summary>
    private static readonly Regex EqualityFilter =
        new("""^\s*(?<attribute>[A-Za-z]\w*)\s+eq\s+"(?<value>[^"]*)"\s*$""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // --- users ------------------------------------------------------------------------------

    public async Task<ScimListResponse<ScimUser>> ListUsersAsync(string? filter, int startIndex, int count)
    {
        var page = Math.Clamp(count <= 0 ? 100 : count, 1, MaxPageSize);
        var skip = Math.Max(0, (startIndex <= 0 ? 1 : startIndex) - 1);

        await using var db = DalService.GetContext(bypassEntityScope: true);

        var query = db.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter))
        {
            var (attribute, value) = ParseFilter(filter);

            query = attribute switch
            {
                "username" => query.Where(u => u.Login == value),
                // NetRisk stores no IdP external id on the user, so externalId is matched against the
                // login — which is what the IdP set it from when it provisioned the account. Reported
                // here rather than silently returning nothing.
                "externalid" => query.Where(u => u.Login == value),
                "emails" or "email" => query.Where(u => u.Email == value),
                "active" => bool.TryParse(value, out var active)
                    ? query.Where(u => u.Enabled == active)
                    : throw new InvalidParameterException("filter", $"'{value}' is not a boolean."),
                _ => throw new InvalidParameterException("filter",
                    $"Filtering on '{attribute}' is not supported. Supported: userName, externalId, "
                    + "emails, active.")
            };
        }

        var total = await query.CountAsync();

        var users = await query
            .OrderBy(u => u.Value)
            .Skip(skip)
            .Take(page)
            .ToListAsync();

        return new ScimListResponse<ScimUser>
        {
            TotalResults = total,
            StartIndex = skip + 1,
            ItemsPerPage = users.Count,
            Resources = users.Select(ToScim).ToList()
        };
    }

    public async Task<ScimUser> GetUserAsync(string id)
    {
        await using var db = DalService.GetContext(bypassEntityScope: true);
        return ToScim(await LoadUserAsync(db, id));
    }

    public async Task<ScimUser> CreateUserAsync(ScimUser user)
    {
        if (user == null || string.IsNullOrWhiteSpace(user.UserName))
            throw new InvalidParameterException(nameof(user.UserName), "userName is required.");

        await using var db = DalService.GetContext(bypassEntityScope: true);

        var login = user.UserName.Trim();

        var existing = await db.Users.FirstOrDefaultAsync(u => u.Login == login);

        if (existing != null)
        {
            // 409 with scimType uniqueness. An IdP retrying a create it already made is normal — a
            // conflict is how it learns to switch to PATCH.
            throw new InvalidParameterException(nameof(user.UserName),
                $"A user with userName '{login}' already exists.");
        }

        var role = await ResolveDefaultRoleAsync(db);

        var stored = new User
        {
            Login = login,
            Name = user.Name?.Best ?? user.DisplayName ?? login,
            Email = user.PrimaryEmail ?? login,
            // No local password: a provisioned account authenticates through the IdP. Random bytes
            // rather than an empty hash so a basic-auth attempt cannot match the empty string.
            Password = RandomNumberGenerator.GetBytes(32),
            Salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)),
            Type = "saml",
            Enabled = user.Active,
            Lockout = (sbyte)(user.Active ? 0 : 1),
            RoleId = role,
            Admin = false,
            Lang = "en",
            LastPasswordChangeDate = DateTime.UtcNow,
            MultiFactor = 0,
            ChangePassword = 0
        };

        db.Users.Add(stored);
        await db.SaveChangesAsync();

        Logger.Information("SCIM created user {Login} (id {Id})", stored.Login, stored.Value);

        await ApplyGroupsAsync(db, stored, user.Groups.Select(g => g.Display ?? g.Value).ToList());

        return ToScim(stored);
    }

    public async Task<ScimUser> ReplaceUserAsync(string id, ScimUser user)
    {
        if (user == null) throw new InvalidParameterException(nameof(user), "A user resource is required.");

        await using var db = DalService.GetContext(bypassEntityScope: true);

        var stored = await LoadUserAsync(db, id);

        if (!string.IsNullOrWhiteSpace(user.UserName) && user.UserName.Trim() != stored.Login)
        {
            var login = user.UserName.Trim();

            if (await db.Users.AnyAsync(u => u.Login == login && u.Value != stored.Value))
                throw new InvalidParameterException(nameof(user.UserName),
                    $"A user with userName '{login}' already exists.");

            stored.Login = login;
        }

        stored.Name = user.Name?.Best ?? user.DisplayName ?? stored.Login;
        stored.Email = user.PrimaryEmail ?? stored.Email;

        ApplyActive(stored, user.Active);

        await db.SaveChangesAsync();

        if (user.Groups.Count > 0)
            await ApplyGroupsAsync(db, stored, user.Groups.Select(g => g.Display ?? g.Value).ToList());

        Logger.Information("SCIM replaced user {Login} (active {Active})", stored.Login, user.Active);

        return ToScim(stored);
    }

    public async Task<ScimUser> PatchUserAsync(string id, ScimPatchRequest patch)
    {
        if (patch == null || patch.Operations.Count == 0)
            throw new InvalidParameterException(nameof(patch), "A PATCH request needs at least one operation.");

        await using var db = DalService.GetContext(bypassEntityScope: true);

        var stored = await LoadUserAsync(db, id);

        foreach (var operation in patch.Operations)
        {
            var op = (operation.Op ?? string.Empty).Trim().ToLowerInvariant();

            if (op is not ("add" or "replace" or "remove"))
                throw new InvalidParameterException(nameof(operation.Op),
                    $"'{operation.Op}' is not a valid PATCH operation.");

            // A path-less replace whose value is an object of attributes. This is the shape Entra ID
            // sends, and an implementation that requires a path never processes an Entra deprovision.
            if (string.IsNullOrWhiteSpace(operation.Path))
            {
                if (op == "remove")
                    throw new InvalidParameterException(nameof(operation.Path),
                        "A remove operation requires a path.");

                if (operation.Value is not { ValueKind: JsonValueKind.Object } bag)
                    throw new InvalidParameterException(nameof(operation.Value),
                        "A path-less operation requires an object value.");

                foreach (var property in bag.EnumerateObject())
                    ApplyAttribute(stored, property.Name, property.Value, "replace");

                continue;
            }

            ApplyAttribute(stored, operation.Path, operation.Value, op);
        }

        await db.SaveChangesAsync();

        Logger.Information("SCIM patched user {Login}: {Operations}", stored.Login,
            string.Join(", ", patch.Operations.Select(o => $"{o.Op} {o.Path ?? "(no path)"}")));

        return ToScim(stored);
    }

    /// <summary>
    /// Applies one attribute. Paths are compared case-insensitively and stripped of the
    /// <c>urn:…:User:</c> prefix some IdPs prepend.
    /// </summary>
    private void ApplyAttribute(User stored, string path, JsonElement? value, string op)
    {
        var attribute = NormalizePath(path);

        switch (attribute)
        {
            case "active":
                // The operation that matters. A remove of "active" means the same as setting it false —
                // the attribute is not optional, so its absence is deactivation.
                var active = op == "remove"
                    ? false
                    : value?.ValueKind switch
                    {
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.String => bool.TryParse(value.Value.GetString(), out var parsed) && parsed,
                        _ => throw new InvalidParameterException("value",
                            "The 'active' attribute takes a boolean.")
                    };

                ApplyActive(stored, active);
                break;

            case "username":
                if (op == "remove")
                    throw new InvalidParameterException("path", "userName cannot be removed.");

                var login = value?.GetString();
                if (!string.IsNullOrWhiteSpace(login)) stored.Login = login.Trim();
                break;

            case "displayname":
            case "name.formatted":
                stored.Name = op == "remove" ? stored.Login : value?.GetString() ?? stored.Name;
                break;

            case "name.givenname":
            case "name.familyname":
                // Composed rather than stored separately: NetRisk has one name column, and letting a
                // givenName patch overwrite the whole name would lose the surname.
                var part = value?.GetString();
                if (!string.IsNullOrWhiteSpace(part) && !stored.Name.Contains(part, StringComparison.Ordinal))
                    stored.Name = attribute.EndsWith("givenname", StringComparison.Ordinal)
                        ? $"{part} {stored.Name}".Trim()
                        : $"{stored.Name} {part}".Trim();
                break;

            case "emails":
            case "emails[type eq \"work\"].value":
                var email = ExtractEmail(value);
                if (!string.IsNullOrWhiteSpace(email)) stored.Email = email;
                break;

            case "externalid":
            case "meta":
            case "groups":
                // Accepted and ignored: NetRisk stores no externalId, group membership is managed by the
                // Groups endpoint, and meta is server-owned. Rejecting these would fail a provisioning
                // cycle over an attribute that carries no information NetRisk keeps.
                Logger.Debug("SCIM patch on {Attribute} for user {Login} accepted and ignored",
                    attribute, stored.Login);
                break;

            default:
                throw new InvalidParameterException("path",
                    $"Patching '{path}' is not supported. Supported: active, userName, displayName, "
                    + "name.formatted, name.givenName, name.familyName, emails.");
        }
    }

    /// <summary>
    /// Sets or clears the account's ability to sign in.
    ///
    /// Both <c>Enabled</c> and <c>Lockout</c> are written because every authenticated request checks
    /// both, and setting only one leaves a path by which a deprovisioned account still authenticates.
    /// </summary>
    private void ApplyActive(User user, bool active)
    {
        var was = user.Enabled == true && user.Lockout == 0;

        user.Enabled = active;
        user.Lockout = (sbyte)(active ? 0 : 1);

        if (was && !active)
            Logger.Warning("SCIM deactivated user {Login} ({Id}); their existing sessions stop working on "
                           + "their next request", user.Login, user.Value);
    }

    public async Task DeactivateUserAsync(string id)
    {
        await using var db = DalService.GetContext(bypassEntityScope: true);

        var stored = await LoadUserAsync(db, id);

        ApplyActive(stored, false);

        await db.SaveChangesAsync();
    }

    // --- groups -----------------------------------------------------------------------------

    public async Task<ScimListResponse<ScimGroup>> ListGroupsAsync(string? filter, int startIndex, int count)
    {
        var page = Math.Clamp(count <= 0 ? 100 : count, 1, MaxPageSize);
        var skip = Math.Max(0, (startIndex <= 0 ? 1 : startIndex) - 1);

        await using var db = DalService.GetContext(bypassEntityScope: true);

        var query = db.Roles.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter))
        {
            var (attribute, value) = ParseFilter(filter);

            query = attribute switch
            {
                "displayname" => query.Where(r => r.Name == value),
                _ => throw new InvalidParameterException("filter",
                    $"Filtering groups on '{attribute}' is not supported. Supported: displayName.")
            };
        }

        var total = await query.CountAsync();

        var roles = await query.OrderBy(r => r.Value).Skip(skip).Take(page).ToListAsync();

        var groups = new List<ScimGroup>();

        foreach (var role in roles) groups.Add(await ToScimAsync(db, role));

        return new ScimListResponse<ScimGroup>
        {
            TotalResults = total,
            StartIndex = skip + 1,
            ItemsPerPage = groups.Count,
            Resources = groups
        };
    }

    public async Task<ScimGroup> GetGroupAsync(string id)
    {
        await using var db = DalService.GetContext(bypassEntityScope: true);
        return await ToScimAsync(db, await LoadRoleAsync(db, id));
    }

    public async Task<ScimGroup> CreateGroupAsync(ScimGroup group)
    {
        if (group == null || string.IsNullOrWhiteSpace(group.DisplayName))
            throw new InvalidParameterException(nameof(group.DisplayName), "displayName is required.");

        await using var db = DalService.GetContext(bypassEntityScope: true);

        var name = group.DisplayName.Trim();

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == name);

        if (role == null)
        {
            // Adopting an existing role of the same name rather than refusing is deliberate: an
            // administrator has almost certainly already created "Security Analysts" by hand, and
            // making the IdP's first sync fail on that is a support call, not a safety feature.
            role = new Role { Name = name, Default = false, Admin = false };
            db.Roles.Add(role);
            await db.SaveChangesAsync();

            Logger.Information("SCIM created role {Role} for group '{Group}'", role.Value, name);
        }

        await SetMembersAsync(db, role, group.Members.Select(m => m.Value).ToList());

        return await ToScimAsync(db, role);
    }

    public async Task<ScimGroup> ReplaceGroupAsync(string id, ScimGroup group)
    {
        if (group == null) throw new InvalidParameterException(nameof(group), "A group resource is required.");

        await using var db = DalService.GetContext(bypassEntityScope: true);

        var role = await LoadRoleAsync(db, id);

        if (!string.IsNullOrWhiteSpace(group.DisplayName)) role.Name = group.DisplayName.Trim();

        await db.SaveChangesAsync();
        await SetMembersAsync(db, role, group.Members.Select(m => m.Value).ToList());

        return await ToScimAsync(db, role);
    }

    public async Task<ScimGroup> PatchGroupAsync(string id, ScimPatchRequest patch)
    {
        if (patch == null || patch.Operations.Count == 0)
            throw new InvalidParameterException(nameof(patch), "A PATCH request needs at least one operation.");

        await using var db = DalService.GetContext(bypassEntityScope: true);

        var role = await LoadRoleAsync(db, id);

        foreach (var operation in patch.Operations)
        {
            var op = (operation.Op ?? string.Empty).Trim().ToLowerInvariant();
            var attribute = NormalizePath(operation.Path ?? string.Empty);

            switch (attribute)
            {
                case "displayname":
                    var name = operation.Value?.GetString();
                    if (!string.IsNullOrWhiteSpace(name)) role.Name = name.Trim();
                    break;

                case "members":
                    var members = ExtractMemberIds(operation.Value);

                    if (op == "remove" && members.Count == 0)
                    {
                        // "remove members" with no value means every member, per the RFC.
                        await SetMembersAsync(db, role, []);
                        break;
                    }

                    await ChangeMembersAsync(db, role, members, add: op != "remove");
                    break;

                default:
                    throw new InvalidParameterException("path",
                        $"Patching group '{operation.Path}' is not supported. Supported: displayName, members.");
            }
        }

        await db.SaveChangesAsync();

        return await ToScimAsync(db, role);
    }

    public async Task DeleteGroupAsync(string id)
    {
        await using var db = DalService.GetContext(bypassEntityScope: true);

        var role = await LoadRoleAsync(db, id);

        // Members are detached, the role itself is kept. Deleting a NetRisk role would strip
        // permissions from anyone else who holds it, and an IdP removing a group from its own scope is
        // not a request to delete a NetRisk role.
        await SetMembersAsync(db, role, []);

        Logger.Information("SCIM emptied role {Role} ({Name}); the role itself was kept", role.Value, role.Name);
    }

    // --- tokens -----------------------------------------------------------------------------

    public async Task<ScimTokenView> IssueTokenAsync(string name, int? identityProviderId, int? createdByUserId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidParameterException(nameof(name), "A provisioning token requires a name.");

        await using var db = DalService.GetContext();

        if (identityProviderId != null
            && !await db.IdentityProviders.AnyAsync(p => p.Id == identityProviderId))
            throw new InvalidParameterException(nameof(identityProviderId),
                $"Identity provider {identityProviderId} was not found.");

        var keyId = Convert.ToHexString(RandomNumberGenerator.GetBytes(KeyIdBytes)).ToLowerInvariant();
        var secret = Base64Url(RandomNumberGenerator.GetBytes(SecretBytes));

        var token = new ScimToken
        {
            Name = name.Trim(),
            KeyId = keyId,
            SecretHash = HashSecret(secret),
            IdentityProviderId = identityProviderId,
            CreatedAt = DateTime.UtcNow,
            CreatedById = createdByUserId
        };

        db.ScimTokens.Add(token);
        await db.SaveChangesAsync();

        Logger.Information("SCIM provisioning token {KeyId} ({Name}) issued by user {User}",
            keyId, token.Name, createdByUserId);

        return new ScimTokenView
        {
            Id = token.Id,
            Name = token.Name,
            KeyId = keyId,
            IdentityProviderId = identityProviderId,
            CreatedAt = token.CreatedAt,
            Secret = Compose(keyId, secret)
        };
    }

    public async Task<List<ScimTokenView>> GetTokensAsync(bool includeRevoked = false)
    {
        await using var db = DalService.GetContext();

        return await db.ScimTokens
            .Where(t => includeRevoked || t.RevokedAt == null)
            .OrderByDescending(t => t.Id)
            .Select(t => new ScimTokenView
            {
                Id = t.Id,
                Name = t.Name,
                KeyId = t.KeyId,
                IdentityProviderId = t.IdentityProviderId,
                CreatedAt = t.CreatedAt,
                LastUsedAt = t.LastUsedAt,
                RevokedAt = t.RevokedAt
            })
            .ToListAsync();
    }

    public async Task<ScimTokenView> RevokeTokenAsync(int id, int? revokedByUserId)
    {
        await using var db = DalService.GetContext();

        var token = await db.ScimTokens.FirstOrDefaultAsync(t => t.Id == id)
                    ?? throw new DataNotFoundException("scim_tokens", id.ToString(),
                        new Exception($"SCIM token {id} was not found."));

        if (token.RevokedAt == null)
        {
            token.RevokedAt = DateTime.UtcNow;
            token.RevokedById = revokedByUserId;
            await db.SaveChangesAsync();

            Logger.Warning("SCIM provisioning token {KeyId} revoked by user {User}",
                token.KeyId, revokedByUserId);
        }

        return new ScimTokenView
        {
            Id = token.Id,
            Name = token.Name,
            KeyId = token.KeyId,
            IdentityProviderId = token.IdentityProviderId,
            CreatedAt = token.CreatedAt,
            LastUsedAt = token.LastUsedAt,
            RevokedAt = token.RevokedAt
        };
    }

    public async Task<ScimToken?> AuthenticateAsync(string presentedToken)
    {
        if (!TryParse(presentedToken, out var keyId, out var secret)) return null;

        await using var db = DalService.GetContext(bypassEntityScope: true);

        var token = await db.ScimTokens.FirstOrDefaultAsync(t => t.KeyId == keyId);

        if (token == null || !token.IsUsable(DateTime.UtcNow)) return null;

        // Fixed-time comparison: an early-exit compare on the stored hash leaks how much of a guessed
        // secret was right.
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(HashSecret(secret)), Encoding.UTF8.GetBytes(token.SecretHash)))
            return null;

        token.LastUsedAt = DateTime.UtcNow;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Recording the use must never fail a valid request.
            Logger.Debug("Could not record SCIM token use: {Message}", ex.Message);
        }

        return token;
    }

    public async Task LogRequestAsync(int? tokenId, string method, string path, int statusCode,
        string? target, string? outcome)
    {
        try
        {
            await using var db = DalService.GetContext(bypassEntityScope: true);

            db.ScimRequestLogs.Add(new ScimRequestLog
            {
                TokenId = tokenId,
                Method = method,
                Path = Truncate(path, 512) ?? string.Empty,
                StatusCode = statusCode,
                Target = Truncate(target, 255),
                Outcome = Truncate(outcome, 512),
                OccurredAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // A failed audit write must not turn a successful provisioning call into an error the IdP
            // retries forever. It is logged loudly instead.
            Logger.Error(ex, "Could not write the SCIM request audit row for {Method} {Path}", method, path);
        }
    }

    public async Task<List<ScimRequestLog>> GetRequestLogAsync(int limit = 200)
    {
        await using var db = DalService.GetContext();

        return await db.ScimRequestLogs
            .OrderByDescending(l => l.Id)
            .Take(Math.Clamp(limit, 1, 1000))
            .ToListAsync();
    }

    // --- helpers ----------------------------------------------------------------------------

    internal static (string Attribute, string Value) ParseFilter(string filter)
    {
        var match = EqualityFilter.Match(filter);

        if (!match.Success)
            throw new InvalidParameterException("filter",
                $"Only simple equality filters are supported (attribute eq \"value\"); received: {filter}");

        return (match.Groups["attribute"].Value.ToLowerInvariant(), match.Groups["value"].Value);
    }

    /// <summary>
    /// Strips the schema URN prefix and lower-cases the path. Several IdPs send
    /// <c>urn:ietf:params:scim:schemas:core:2.0:User:active</c> where the RFC's examples say
    /// <c>active</c>, and treating those as different attributes fails the deprovision.
    /// </summary>
    internal static string NormalizePath(string path)
    {
        var trimmed = path.Trim();

        var colon = trimmed.LastIndexOf(':');
        if (colon >= 0 && colon < trimmed.Length - 1) trimmed = trimmed[(colon + 1)..];

        return trimmed.ToLowerInvariant();
    }

    private static string? ExtractEmail(JsonElement? value)
    {
        if (value == null) return null;

        return value.Value.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString(),
            JsonValueKind.Object => value.Value.TryGetProperty("value", out var single)
                ? single.GetString()
                : null,
            // An array of email objects: the primary one, or the first.
            JsonValueKind.Array => value.Value.EnumerateArray()
                                       .Where(e => e.ValueKind == JsonValueKind.Object)
                                       .OrderByDescending(e => e.TryGetProperty("primary", out var p)
                                                               && p.ValueKind == JsonValueKind.True)
                                       .Select(e => e.TryGetProperty("value", out var v) ? v.GetString() : null)
                                       .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)),
            _ => null
        };
    }

    private static List<string> ExtractMemberIds(JsonElement? value)
    {
        if (value == null) return [];

        return value.Value.ValueKind switch
        {
            JsonValueKind.String => [value.Value.GetString() ?? string.Empty],
            JsonValueKind.Object => value.Value.TryGetProperty("value", out var single)
                ? [single.GetString() ?? string.Empty]
                : [],
            JsonValueKind.Array => value.Value.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("value", out var v)
                    ? v.GetString()
                    : e.GetString())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .ToList(),
            _ => []
        };
    }

    /// <summary>Puts exactly <paramref name="memberIds"/> in the role, adding and removing as needed.</summary>
    private async Task SetMembersAsync(AuditableContext db, Role role, List<string> memberIds)
    {
        var wanted = memberIds.Select(ToUserId).Where(id => id != null).Select(id => id!.Value).ToHashSet();

        var current = await db.Users.Where(u => u.RoleId == role.Value).ToListAsync();

        foreach (var user in current.Where(u => !wanted.Contains(u.Value)))
        {
            // Removed members fall back to the default role rather than to role 0: a user with no role
            // has no permissions at all, which is a harsher outcome than the IdP asked for.
            user.RoleId = await ResolveDefaultRoleAsync(db);
        }

        foreach (var userId in wanted)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Value == userId);
            if (user != null) user.RoleId = role.Value;
        }

        await db.SaveChangesAsync();
    }

    private async Task ChangeMembersAsync(AuditableContext db, Role role, List<string> memberIds, bool add)
    {
        foreach (var id in memberIds)
        {
            var userId = ToUserId(id);
            if (userId == null) continue;

            var user = await db.Users.FirstOrDefaultAsync(u => u.Value == userId);
            if (user == null) continue;

            if (add) user.RoleId = role.Value;
            else if (user.RoleId == role.Value) user.RoleId = await ResolveDefaultRoleAsync(db);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Applies an identity provider's group mapping to a freshly provisioned user, so a SCIM create
    /// that carries groups lands the user in the right role rather than the default one.
    /// </summary>
    private async Task ApplyGroupsAsync(AuditableContext db, User user, List<string> groupNames)
    {
        if (groupNames.Count == 0) return;

        var mappings = await db.IdentityProviders
            .Where(p => p.GroupMappingJson != null)
            .Select(p => p.GroupMappingJson!)
            .ToListAsync();

        foreach (var json in mappings)
        {
            var mapping = IdentityProvidersService.ParseGroupMapping(json);

            foreach (var group in groupNames)
            {
                if (!mapping.TryGetValue(group, out var target)) continue;

                if (target.Role != null)
                {
                    var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == target.Role);
                    if (role != null) user.RoleId = role.Value;
                }

                if (target.Admin) user.Admin = true;
            }
        }

        // Falls back to a role of the same name as the group, which is the convention this service
        // establishes for groups nobody has mapped explicitly.
        if (user.RoleId == 0)
        {
            foreach (var group in groupNames)
            {
                var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == group);
                if (role == null) continue;

                user.RoleId = role.Value;
                break;
            }
        }

        await db.SaveChangesAsync();
    }

    private static async Task<int> ResolveDefaultRoleAsync(AuditableContext db)
    {
        var defaultRole = await db.Roles.FirstOrDefaultAsync(r => r.Default == true);
        return defaultRole?.Value ?? 0;
    }

    private static int? ToUserId(string? id) => int.TryParse(id, out var parsed) ? parsed : null;

    private static async Task<User> LoadUserAsync(AuditableContext db, string id)
    {
        var userId = ToUserId(id)
                     ?? throw new DataNotFoundException("user", id,
                         new Exception($"'{id}' is not a NetRisk user id."));

        return await db.Users.FirstOrDefaultAsync(u => u.Value == userId)
               ?? throw new DataNotFoundException("user", id, new Exception($"User {id} was not found."));
    }

    private static async Task<Role> LoadRoleAsync(AuditableContext db, string id)
    {
        var roleId = ToUserId(id)
                     ?? throw new DataNotFoundException("role", id,
                         new Exception($"'{id}' is not a NetRisk role id."));

        return await db.Roles.FirstOrDefaultAsync(r => r.Value == roleId)
               ?? throw new DataNotFoundException("role", id, new Exception($"Group {id} was not found."));
    }

    private static ScimUser ToScim(User user) => new()
    {
        Id = user.Value.ToString(),
        ExternalId = user.Login,
        UserName = user.Login,
        DisplayName = user.Name,
        Name = new ScimName { Formatted = user.Name },
        Emails = string.IsNullOrWhiteSpace(user.Email)
            ? []
            : [new ScimEmail { Value = user.Email, Type = "work", Primary = true }],
        Active = user.Enabled == true && user.Lockout == 0,
        Meta = new ScimMeta
        {
            ResourceType = "User",
            Location = $"/scim/v2/Users/{user.Value}"
        }
    };

    private static async Task<ScimGroup> ToScimAsync(AuditableContext db, Role role)
    {
        var members = await db.Users
            .Where(u => u.RoleId == role.Value)
            .Select(u => new ScimMember { Value = u.Value.ToString(), Display = u.Name })
            .ToListAsync();

        return new ScimGroup
        {
            Id = role.Value.ToString(),
            DisplayName = role.Name,
            Members = members,
            Meta = new ScimMeta
            {
                ResourceType = "Group",
                Location = $"/scim/v2/Groups/{role.Value}"
            }
        };
    }

    internal static string HashSecret(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    internal static string Compose(string keyId, string secret) =>
        $"{ScimToken.SecretPrefix}{keyId}_{secret}";

    internal static bool TryParse(string presented, out string keyId, out string secret)
    {
        keyId = string.Empty;
        secret = string.Empty;

        if (string.IsNullOrWhiteSpace(presented)) return false;
        if (!presented.StartsWith(ScimToken.SecretPrefix, StringComparison.Ordinal)) return false;

        var remainder = presented[ScimToken.SecretPrefix.Length..];
        var separator = remainder.IndexOf('_');

        if (separator <= 0 || separator == remainder.Length - 1) return false;

        keyId = remainder[..separator];
        secret = remainder[(separator + 1)..];

        return true;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? Truncate(string? text, int max) =>
        text == null || text.Length <= max ? text : text[..(max - 1)] + "…";
}
