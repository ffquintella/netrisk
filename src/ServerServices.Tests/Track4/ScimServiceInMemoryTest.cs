using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Model.Authentication.Scim;
using Model.Exceptions;
using ServerServices.Auth;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track4;

/// <summary>
/// SCIM 2.0 provisioning (Track 4 milestone 4.3.2).
///
/// The milestone's non-negotiable is here: <c>active:false</c> must actually disable login. Both Entra
/// ID and Okta send that as a PATCH, and Entra sends it without a path — an implementation that only
/// handles PUT, or that requires a path, never disables anyone and nobody notices until an auditor
/// asks why a leaver still has an account.
/// </summary>
[TestSubject(typeof(ScimService))]
public class ScimServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IScimService _svc;

    public ScimServiceInMemoryTest()
    {
        _svc = GetService<IScimService>();

        Seed(ctx =>
        {
            ctx.Roles.Add(new Role { Value = 1, Name = "Analyst", Default = true, Admin = false });
            ctx.Roles.Add(new Role { Value = 2, Name = "Security Admins", Default = false, Admin = false });

            ctx.Users.Add(NewUser(1, "alice@acme.com", roleId: 1));
            ctx.Users.Add(NewUser(2, "bob@acme.com", roleId: 2));
        });
    }

    private static User NewUser(int id, string login, int roleId, bool enabled = true) => new()
    {
        Value = id, Login = login, Name = login.Split('@')[0], Email = login,
        Enabled = enabled, Lockout = (sbyte)(enabled ? 0 : 1), Type = "local", Salt = "s",
        Password = Encoding.UTF8.GetBytes("p"), RoleId = roleId
    };

    private static ScimPatchRequest Patch(string op, string? path, object? value)
    {
        var element = value == null
            ? (JsonElement?)null
            : JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(value));

        return new ScimPatchRequest
        {
            Operations = [new ScimPatchOperation { Op = op, Path = path, Value = element }]
        };
    }

    // --- listing and filtering --------------------------------------------------------------

    [Fact]
    public async Task ListingPagesTheSciWayWithA1BasedStartIndex()
    {
        var page = await _svc.ListUsersAsync(null, startIndex: 2, count: 1);

        Assert.Equal(2, page.TotalResults);
        Assert.Equal(2, page.StartIndex);
        Assert.Equal(1, page.ItemsPerPage);
        Assert.Equal("bob@acme.com", Assert.Single(page.Resources).UserName);
    }

    [Fact]
    public async Task TheUserNameFilterIsTheOneAnIdpActuallySends()
    {
        var page = await _svc.ListUsersAsync("""userName eq "alice@acme.com" """, 1, 100);

        Assert.Equal(1, page.TotalResults);
        Assert.Equal("alice@acme.com", page.Resources[0].UserName);
    }

    [Fact]
    public async Task AnUnsupportedFilterIsRefusedRatherThanIgnored()
    {
        // Silently ignoring an unsupported filter would return the whole directory to a caller that
        // asked for one user.
        var thrown = await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.ListUsersAsync("""title eq "CISO" """, 1, 100));

        Assert.Contains("not supported", thrown.Message);
    }

    [Fact]
    public async Task AFilterThatIsNotSimpleEqualityIsRefused()
    {
        await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.ListUsersAsync("""userName sw "alice" """, 1, 100));
    }

    [Fact]
    public async Task ThePageSizeIsCapped()
    {
        var page = await _svc.ListUsersAsync(null, 1, count: 10_000);

        Assert.True(page.ItemsPerPage <= 200);
    }

    // --- create -----------------------------------------------------------------------------

    [Fact]
    public async Task CreatingAUserProvisionsADisabledPasswordAccount()
    {
        var created = await _svc.CreateUserAsync(new ScimUser
        {
            UserName = "carol@acme.com",
            Name = new ScimName { GivenName = "Carol", FamilyName = "Chen" },
            Emails = [new ScimEmail { Value = "carol@acme.com", Primary = true }],
            Active = true
        });

        Assert.NotNull(created.Id);

        await using var db = OpenContext();
        var user = db.Users.Single(u => u.Login == "carol@acme.com");

        Assert.Equal("Carol Chen", user.Name);
        Assert.True(user.Enabled);
        // No usable local password: a provisioned account authenticates through the IdP.
        Assert.NotEmpty(user.Password);
        Assert.Equal(1, user.RoleId);
    }

    [Fact]
    public async Task ADuplicateUserNameIsAConflict()
    {
        // An IdP retrying a create it already made is normal; a conflict is how it learns to PATCH.
        var thrown = await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.CreateUserAsync(new ScimUser { UserName = "alice@acme.com" }));

        Assert.Contains("already exists", thrown.Message);
    }

    [Fact]
    public async Task CreatingWithoutAUserNameIsRefused()
    {
        await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.CreateUserAsync(new ScimUser()));
    }

    [Fact]
    public async Task AUserCreatedInactiveCannotSignIn()
    {
        await _svc.CreateUserAsync(new ScimUser { UserName = "dan@acme.com", Active = false });

        await using var db = OpenContext();
        var user = db.Users.Single(u => u.Login == "dan@acme.com");

        Assert.False(user.Enabled);
        Assert.Equal(1, user.Lockout);
    }

    // --- deactivation, the operation that matters -------------------------------------------

    [Fact]
    public async Task PatchingActiveToFalseDisablesLoginBothWays()
    {
        var patched = await _svc.PatchUserAsync("1", Patch("replace", "active", false));

        Assert.False(patched.Active);

        await using var db = OpenContext();
        var user = db.Users.Single(u => u.Value == 1);

        // Every authenticated request checks both; setting only one leaves a path by which a
        // deprovisioned account still authenticates.
        Assert.False(user.Enabled);
        Assert.Equal(1, user.Lockout);
    }

    [Fact]
    public async Task APathLessReplaceIsHonouredBecauseThatIsWhatEntraIdSends()
    {
        await _svc.PatchUserAsync("1", Patch("replace", null, new { active = false }));

        await using var db = OpenContext();
        Assert.False(db.Users.Single(u => u.Value == 1).Enabled);
    }

    [Fact]
    public async Task ASchemaUrnPrefixedPathIsHonoured()
    {
        // Several IdPs send urn:ietf:params:scim:schemas:core:2.0:User:active where the RFC's examples
        // say "active"; treating those as different attributes fails the deprovision.
        await _svc.PatchUserAsync("1",
            Patch("replace", "urn:ietf:params:scim:schemas:core:2.0:User:active", false));

        await using var db = OpenContext();
        Assert.False(db.Users.Single(u => u.Value == 1).Enabled);
    }

    [Fact]
    public async Task ARemoveOfActiveAlsoDeactivates()
    {
        await _svc.PatchUserAsync("1", Patch("remove", "active", null));

        await using var db = OpenContext();
        Assert.False(db.Users.Single(u => u.Value == 1).Enabled);
    }

    [Fact]
    public async Task ReactivationWorksToo()
    {
        await _svc.PatchUserAsync("1", Patch("replace", "active", false));
        await _svc.PatchUserAsync("1", Patch("replace", "active", true));

        await using var db = OpenContext();
        var user = db.Users.Single(u => u.Value == 1);

        Assert.True(user.Enabled);
        Assert.Equal(0, user.Lockout);
    }

    [Fact]
    public async Task DeleteDeactivatesRatherThanRemovingTheRow()
    {
        await _svc.DeactivateUserAsync("1");

        await using var db = OpenContext();

        // A NetRisk user is referenced by risks, findings and audit history; hard-deleting them would
        // fail on a constraint or erase attribution.
        Assert.Equal(2, db.Users.Count());
        Assert.False(db.Users.Single(u => u.Value == 1).Enabled);
    }

    [Fact]
    public async Task ReplacingAUserAppliesTheActiveFlagAndTheName()
    {
        await _svc.ReplaceUserAsync("1", new ScimUser
        {
            UserName = "alice@acme.com",
            DisplayName = "Alice A. Adams",
            Emails = [new ScimEmail { Value = "alice.adams@acme.com", Primary = true }],
            Active = false
        });

        await using var db = OpenContext();
        var user = db.Users.Single(u => u.Value == 1);

        Assert.Equal("Alice A. Adams", user.Name);
        Assert.Equal("alice.adams@acme.com", user.Email);
        Assert.False(user.Enabled);
    }

    [Fact]
    public async Task PatchingAnEmailArrayTakesThePrimaryAddress()
    {
        await _svc.PatchUserAsync("1", Patch("replace", "emails", new[]
        {
            new { value = "old@acme.com", primary = false },
            new { value = "new@acme.com", primary = true }
        }));

        await using var db = OpenContext();
        Assert.Equal("new@acme.com", db.Users.Single(u => u.Value == 1).Email);
    }

    [Fact]
    public async Task AnUnsupportedPatchPathIsRefusedWithTheSupportedList()
    {
        var thrown = await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.PatchUserAsync("1", Patch("replace", "title", "CISO")));

        Assert.Contains("active", thrown.Message);
    }

    [Fact]
    public async Task AttributesNetRiskDoesNotStoreAreAcceptedAndIgnored()
    {
        // Rejecting these would fail a whole provisioning cycle over an attribute that carries no
        // information NetRisk keeps.
        var patched = await _svc.PatchUserAsync("1", Patch("replace", "externalId", "abc-123"));

        Assert.True(patched.Active);
    }

    [Fact]
    public async Task AnInvalidPatchOperationIsRefused()
    {
        await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.PatchUserAsync("1", Patch("frobnicate", "active", false)));
    }

    [Fact]
    public async Task AnEmptyPatchIsRefused()
    {
        await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.PatchUserAsync("1", new ScimPatchRequest()));
    }

    [Fact]
    public async Task AnUnknownUserIsNotFound()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetUserAsync("404"));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetUserAsync("not-a-number"));
    }

    // --- groups -----------------------------------------------------------------------------

    [Fact]
    public async Task AScimGroupIsANetRiskRole()
    {
        var group = await _svc.GetGroupAsync("2");

        Assert.Equal("Security Admins", group.DisplayName);
        Assert.Equal("bob@acme.com", Assert.Single(group.Members).Display is null
            ? "bob@acme.com"
            : group.Members[0].Value == "2" ? "bob@acme.com" : "");
    }

    [Fact]
    public async Task CreatingAGroupAdoptsAnExistingRoleOfTheSameName()
    {
        var group = await _svc.CreateGroupAsync(new ScimGroup { DisplayName = "Security Admins" });

        // An administrator has almost certainly created it by hand already; failing the IdP's first sync
        // on that is a support call, not a safety feature.
        Assert.Equal("2", group.Id);

        await using var db = OpenContext();
        Assert.Equal(2, db.Roles.Count());
    }

    [Fact]
    public async Task CreatingAGroupWithMembersMovesThemIntoTheRole()
    {
        var group = await _svc.CreateGroupAsync(new ScimGroup
        {
            DisplayName = "Incident Responders",
            Members = [new ScimMember { Value = "1" }]
        });

        await using var db = OpenContext();
        var role = db.Roles.Single(r => r.Name == "Incident Responders");

        Assert.Equal(role.Value, db.Users.Single(u => u.Value == 1).RoleId);
        Assert.Single(group.Members);
    }

    [Fact]
    public async Task PatchingAGroupAddsAndRemovesMembers()
    {
        await _svc.PatchGroupAsync("2", Patch("add", "members", new[] { new { value = "1" } }));

        await using (var db = OpenContext())
            Assert.Equal(2, db.Users.Single(u => u.Value == 1).RoleId);

        await _svc.PatchGroupAsync("2", Patch("remove", "members", new[] { new { value = "1" } }));

        await using (var db = OpenContext())
            // Falls back to the default role rather than to no role at all — a user with no role has no
            // permissions, which is harsher than the IdP asked for.
            Assert.Equal(1, db.Users.Single(u => u.Value == 1).RoleId);
    }

    [Fact]
    public async Task RemovingEveryMemberEmptiesTheGroup()
    {
        await _svc.PatchGroupAsync("2", Patch("remove", "members", null));

        await using var db = OpenContext();
        Assert.Equal(1, db.Users.Single(u => u.Value == 2).RoleId);
    }

    [Fact]
    public async Task DeletingAGroupEmptiesItButKeepsTheRole()
    {
        await _svc.DeleteGroupAsync("2");

        await using var db = OpenContext();

        // Deleting a NetRisk role would strip permissions from anyone else who holds it, and an IdP
        // removing a group from its own scope is not a request to do that.
        Assert.Equal(2, db.Roles.Count());
        Assert.Equal(1, db.Users.Single(u => u.Value == 2).RoleId);
    }

    [Fact]
    public async Task GroupsCanBeFilteredByDisplayName()
    {
        var page = await _svc.ListGroupsAsync("""displayName eq "Security Admins" """, 1, 100);

        Assert.Equal(1, page.TotalResults);
    }

    [Fact]
    public async Task AnUnsupportedGroupFilterIsRefused()
    {
        await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.ListGroupsAsync("""externalId eq "x" """, 1, 100));
    }

    // --- tokens and audit -------------------------------------------------------------------

    [Fact]
    public async Task AProvisioningTokenIsShownOnceAndStoredHashed()
    {
        var issued = await _svc.IssueTokenAsync("Entra ID provisioning", null, createdByUserId: 1);

        Assert.NotNull(issued.Secret);
        Assert.StartsWith(ScimToken.SecretPrefix, issued.Secret);

        await using var db = OpenContext();
        var stored = db.ScimTokens.Single();

        Assert.NotEqual(issued.Secret, stored.SecretHash);

        // No read path can produce it again.
        var listed = Assert.Single(await _svc.GetTokensAsync());
        Assert.Null(listed.Secret);
    }

    [Fact]
    public async Task AnIssuedTokenAuthenticatesAndARevokedOneDoesNot()
    {
        var issued = await _svc.IssueTokenAsync("Okta", null, 1);

        Assert.NotNull(await _svc.AuthenticateAsync(issued.Secret!));

        await _svc.RevokeTokenAsync(issued.Id, 1);

        Assert.Null(await _svc.AuthenticateAsync(issued.Secret!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nrk_deadbeef_secret")]
    [InlineData("scim_")]
    [InlineData("scim_keyid")]
    public async Task AMalformedOrForeignTokenDoesNotAuthenticate(string presented)
    {
        Assert.Null(await _svc.AuthenticateAsync(presented));
    }

    [Fact]
    public async Task AWrongSecretWithAValidKeyIdDoesNotAuthenticate()
    {
        var issued = await _svc.IssueTokenAsync("Okta", null, 1);

        var keyId = issued.KeyId;

        Assert.Null(await _svc.AuthenticateAsync($"{ScimToken.SecretPrefix}{keyId}_wrong-secret"));
    }

    [Fact]
    public async Task IssuingWithoutANameIsRefused()
    {
        await Assert.ThrowsAsync<InvalidParameterException>(() => _svc.IssueTokenAsync(" ", null, 1));
    }

    [Fact]
    public async Task IssuingAgainstAnUnknownIdentityProviderIsRefused()
    {
        await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.IssueTokenAsync("x", identityProviderId: 999, 1));
    }

    [Fact]
    public async Task RevokingAnUnknownTokenIsNotFound()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.RevokeTokenAsync(404, 1));
    }

    [Fact]
    public async Task EveryRequestIsAudited()
    {
        var issued = await _svc.IssueTokenAsync("Entra", null, 1);

        await _svc.LogRequestAsync(issued.Id, "PATCH", "/scim/v2/Users/1", 200, "alice@acme.com",
            "patched user: replace active");

        var log = Assert.Single(await _svc.GetRequestLogAsync());

        // "When did the IdP disable this user" is a question asked during incidents, not during
        // development.
        Assert.Equal("PATCH", log.Method);
        Assert.Equal(200, log.StatusCode);
        Assert.Contains("active", log.Outcome!);
    }

    [Fact]
    public void TokenParsingRoundTrips()
    {
        var composed = ScimService.Compose("abcd1234", "secret-half");

        Assert.True(ScimService.TryParse(composed, out var keyId, out var secret));
        Assert.Equal("abcd1234", keyId);
        Assert.Equal("secret-half", secret);
    }

    [Theory]
    [InlineData("active", "active")]
    [InlineData("urn:ietf:params:scim:schemas:core:2.0:User:active", "active")]
    [InlineData("name.givenName", "name.givenname")]
    public void PathsAreNormalised(string input, string expected)
    {
        Assert.Equal(expected, ScimService.NormalizePath(input));
    }
}
