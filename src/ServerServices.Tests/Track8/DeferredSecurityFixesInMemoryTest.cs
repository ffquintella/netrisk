using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Model.Exceptions;
using ServerServices.Interfaces;
using ServerServices.Security;
using ServerServices.Services;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track8;

/// <summary>
/// The security findings Track 7 deferred to Track 8: per-session token revocation (NR-2026-028),
/// the shared brute-force counter (NR-2026-008b) and per-file access control (NR-2026-017).
///
/// Each is asserted from the refusing side, because the pre-fix behaviour of all three was "allow" —
/// a test that only shows the allowed case would have passed before the fix as well.
/// </summary>
public class DeferredSecurityFixesInMemoryTest : InMemoryServiceTestBase
{
    private static User NewUser(int id, string name, bool admin = false) => new()
    {
        Value = id, Name = name, Login = name, Enabled = true, Admin = admin,
        Type = "local", Salt = "s", Password = Encoding.UTF8.GetBytes("p"), Email = $"{name}@x.test"
    };

    private static Risk NewRisk(int id, int? entityId = null, int? owner = null) => new()
    {
        Id = id, Status = "New", Subject = $"Risk {id}", ReferenceId = $"R-{id}",
        Assessment = string.Empty, Notes = string.Empty,
        RiskCatalogMapping = string.Empty, ThreatCatalogMapping = string.Empty,
        SubmissionDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        LastUpdate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        EntityId = entityId, Owner = owner
    };

    // --- NR-2026-028: per-session revocation --------------------------------------------------------

    [Fact]
    public async Task TestARevokedTokenIsReportedAsRevoked()
    {
        TokenRevocationService.ClearCache();

        Seed(ctx => ctx.Users.Add(NewUser(1, "alice")));

        var service = GetService<ITokenRevocationService>();

        Assert.False(await service.IsRevokedAsync("jti-1"));

        await service.RevokeAsync("jti-1", 1, DateTime.UtcNow.AddMinutes(60), "user signed out");

        Assert.True(await service.IsRevokedAsync("jti-1"));
    }

    [Fact]
    public async Task TestRevokingTwiceIsNotAnError()
    {
        TokenRevocationService.ClearCache();

        Seed(ctx => ctx.Users.Add(NewUser(1, "alice")));

        var service = GetService<ITokenRevocationService>();

        await service.RevokeAsync("jti-2", 1, DateTime.UtcNow.AddMinutes(60));
        // A client that retried a failed sign-out did nothing wrong.
        await service.RevokeAsync("jti-2", 1, DateTime.UtcNow.AddMinutes(60));

        await using var db = OpenContext();
        Assert.Single(db.RevokedTokens.Where(t => t.Jti == "jti-2").ToList());
    }

    [Fact]
    public async Task TestATokenWithNoJtiCannotBeRevokedIndividually()
    {
        var service = GetService<ITokenRevocationService>();

        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            service.RevokeAsync("  ", 1, DateTime.UtcNow.AddMinutes(60)));

        // ...and asking about one is simply "not revoked" rather than an error, so the handler's
        // fast path does not need a special case.
        Assert.False(await service.IsRevokedAsync(null!));
    }

    [Fact]
    public async Task TestPruningRemovesOnlyRowsWhoseTokenHasExpired()
    {
        TokenRevocationService.ClearCache();

        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(1, "alice"));
            ctx.RevokedTokens.Add(new RevokedToken
            {
                Id = 1, Jti = "old", UserId = 1, RevokedAt = DateTime.UtcNow.AddDays(-2),
                ExpiresAt = DateTime.UtcNow.AddDays(-1)
            });
            ctx.RevokedTokens.Add(new RevokedToken
            {
                Id = 2, Jti = "live", UserId = 1, RevokedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });
        });

        var removed = await GetService<ITokenRevocationService>().PruneExpiredAsync(DateTime.UtcNow);

        Assert.Equal(1, removed);

        await using var db = OpenContext();
        Assert.Single(db.RevokedTokens.ToList());
        Assert.Equal("live", db.RevokedTokens.Single().Jti);
    }

    // --- NR-2026-008b: the shared brute-force counter -----------------------------------------------

    private PersistedLoginAttemptTracker NewTracker() =>
        new(Serilog.Log.Logger, GetService<IDalService>(),
            new LoginAttemptTracker(Serilog.Log.Logger));

    [Fact]
    public void TestFailuresAreCountedInTheDatabaseSoEveryInstanceSeesThem()
    {
        var first = NewTracker();

        for (var i = 0; i < PersistedLoginAttemptTracker.DefaultMaxFailures; i++)
            first.RegisterFailure("alice", "10.0.0.1");

        // A *different* tracker instance — standing in for a second API process behind the load
        // balancer, which before this fix had its own untouched budget.
        var second = NewTracker();

        var state = second.Check("alice", "10.0.0.1");

        Assert.True(state.IsLockedOut);
        Assert.True(state.RetryAfter > TimeSpan.Zero);
    }

    [Fact]
    public void TestASuccessfulLoginClearsTheSharedCounter()
    {
        var tracker = NewTracker();

        for (var i = 0; i < PersistedLoginAttemptTracker.DefaultMaxFailures; i++)
            tracker.RegisterFailure("bob", "10.0.0.2");

        tracker.RegisterSuccess("bob", "10.0.0.2");

        Assert.False(NewTracker().Check("bob", "10.0.0.2").IsLockedOut);

        using var db = OpenContext();
        Assert.Empty(db.LoginAttempts.Where(a => a.Identity == "bob").ToList());
    }

    [Fact]
    public void TestTheCounterIsKeyedOnIdentityAndSourceTogether()
    {
        var tracker = NewTracker();

        for (var i = 0; i < PersistedLoginAttemptTracker.DefaultMaxFailures; i++)
            tracker.RegisterFailure("carol", "10.0.0.3");

        // The same account from a different address is a different row, so one attacker cannot lock
        // a colleague out of every office.
        Assert.False(NewTracker().Check("carol", "10.0.0.4").IsLockedOut);
    }

    [Fact]
    public void TestAnAttemptWithNoIdentityIsNotPersisted()
    {
        var tracker = NewTracker();

        tracker.RegisterFailure(null, "10.0.0.5");

        using var db = OpenContext();
        Assert.Empty(db.LoginAttempts.ToList());
    }

    [Fact]
    public void TestTheIdentityIsLowerCasedSoCaseVariationsShareABudget()
    {
        var tracker = NewTracker();

        for (var i = 0; i < PersistedLoginAttemptTracker.DefaultMaxFailures; i++)
            tracker.RegisterFailure("Dave", "10.0.0.6");

        Assert.True(NewTracker().Check("dave", "10.0.0.6").IsLockedOut);
    }

    // --- NR-2026-017: per-file access control -------------------------------------------------------

    private IFileAccessAuthorizer Authorizer => GetService<IFileAccessAuthorizer>();

    private static NrFile NewFile(int id, int uploader) => new()
    {
        Id = id, Name = $"file-{id}.pdf", UniqueName = $"unique-{id}", User = uploader,
        Content = [1, 2, 3], Size = 3, Timestamp = DateTime.UtcNow
    };

    [Fact]
    public async Task TestTheUploaderCanAlwaysReadBackTheirOwnUpload()
    {
        Seed(ctx => ctx.Users.Add(NewUser(1, "alice")));

        // No parent yet — that is what an upload mid-dialog looks like.
        await Authorizer.EnsureCanReadAsync(NewFile(1, uploader: 1), NewUser(1, "alice"));
    }

    [Fact]
    public async Task TestAParentlessFileIsRefusedToEveryoneElse()
    {
        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(1, "alice"));
            ctx.Users.Add(NewUser(2, "mallory"));
        });

        await Assert.ThrowsAsync<UserNotAuthorizedException>(() =>
            Authorizer.EnsureCanReadAsync(NewFile(1, uploader: 1), NewUser(2, "mallory")));
    }

    [Fact]
    public async Task TestAnAdministratorCanReadAnything()
    {
        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(1, "alice"));
            ctx.Users.Add(NewUser(2, "root", admin: true));
        });

        await Authorizer.EnsureCanReadAsync(NewFile(1, uploader: 1), NewUser(2, "root", admin: true));
    }

    /// <summary>
    /// The finding in one test: before this, any authenticated user who knew the unique name could
    /// download a risk attachment regardless of whether they could see the risk.
    /// </summary>
    [Fact]
    public async Task TestARiskAttachmentIsRefusedWithoutTheRiskPermission()
    {
        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(1, "alice"));
            ctx.Users.Add(NewUser(2, "mallory"));
            ctx.Risks.Add(NewRisk(1, owner: 1));
        });

        var file = NewFile(1, uploader: 1);
        file.RiskId = 1;

        await Assert.ThrowsAsync<UserNotAuthorizedException>(() =>
            Authorizer.EnsureCanReadAsync(file, NewUser(2, "mallory")));
    }

    [Fact]
    public async Task TestARiskAttachmentIsReadableByTheRiskOwnerWithoutTheBlanketPermission()
    {
        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(1, "alice"));
            ctx.Users.Add(NewUser(3, "riskowner"));
            ctx.Risks.Add(NewRisk(1, owner: 3));
        });

        var file = NewFile(1, uploader: 1);
        file.RiskId = 1;

        // The register's own relationship rules are the fallback: somebody who can see the risk can
        // see what is attached to it.
        await Authorizer.EnsureCanReadAsync(file, NewUser(3, "riskowner"));
    }

    [Fact]
    public async Task TestAMitigationAttachmentInheritsTheRisksRules()
    {
        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(1, "alice"));
            ctx.Users.Add(NewUser(3, "riskowner"));
            // The permission lookup reads the user row, so a stranger has to exist to be refused for
            // the right reason.
            ctx.Users.Add(NewUser(4, "stranger"));
            ctx.Risks.Add(NewRisk(1, owner: 3));
            ctx.Mitigations.Add(new Mitigation
            {
                Id = 1, RiskId = 1, PlanningStrategy = 1, MitigationEffort = 1, MitigationCost = 1,
                MitigationOwner = 3, SubmittedBy = 3, MitigationPercent = 0,
                CurrentSolution = string.Empty, SecurityRequirements = string.Empty,
                SecurityRecommendations = string.Empty,
                SubmissionDate = DateTime.UtcNow, LastUpdate = DateTime.UtcNow,
                PlanningDate = new DateOnly(2026, 6, 1)
            });
        });

        var file = NewFile(1, uploader: 1);
        file.MitigationId = 1;

        await Authorizer.EnsureCanReadAsync(file, NewUser(3, "riskowner"));

        await Assert.ThrowsAsync<UserNotAuthorizedException>(() =>
            Authorizer.EnsureCanReadAsync(file, NewUser(4, "stranger")));
    }

    /// <summary>
    /// The query filter is the other half of the fix, and the half that closes cross-tenant reads
    /// outright: a file belonging to another business entity is simply not found.
    /// </summary>
    [Fact]
    public void TestAnAttachmentOfAnotherEntityIsInvisibleToAScopedCaller()
    {
        SeedUnscoped(ctx =>
        {
            var mine = NewFile(1, uploader: 1);
            mine.EntityId = 1;
            ctx.NrFiles.Add(mine);

            var theirs = NewFile(2, uploader: 1);
            theirs.EntityId = 2;
            ctx.NrFiles.Add(theirs);

            // A legacy row with no entity stays visible: hiding every existing attachment from every
            // scoped user would be a data-loss-shaped regression rather than a fix.
            ctx.NrFiles.Add(NewFile(3, uploader: 1));
        });

        ScopeTo(1);

        using var db = OpenContext();
        var visible = db.NrFiles.Select(f => f.Id).OrderBy(id => id).ToList();

        Assert.Equal([1, 3], visible);
    }
}
