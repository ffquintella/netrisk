using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using ServerServices.Findings;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track3;

/// <summary>
/// Finding triage lifecycle, its audit trail, and risk acceptance (Track 3 milestone 3.2), against a
/// real EF context.
///
/// The invariant these tests are really guarding: a status never moves without a history row landing
/// in the same save. A timeline with gaps is not evidence of anything, and a service that writes one
/// without the other is indistinguishable from tampering.
/// </summary>
[TestSubject(typeof(FindingLifecycleService))]
public class FindingLifecycleServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IFindingLifecycleService _svc;

    public FindingLifecycleServiceInMemoryTest()
    {
        _svc = GetService<IFindingLifecycleService>();
    }

    private static Vulnerability NewFinding(int id, FindingStatus status = FindingStatus.Active) => new()
    {
        Id = id,
        Title = $"Finding {id}",
        FirstDetection = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        LastDetection = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        DetectionCount = 1,
        Status = 1,
        LifecycleStatus = status
    };

    private static User NewUser(int id, string name) => new()
    {
        Value = id, Name = name, Login = name, Enabled = true,
        Type = "local", Salt = "s", Password = System.Text.Encoding.UTF8.GetBytes("p"), Email = $"{name}@x"
    };

    private RiskAcceptance NewAcceptance(string name = "Q3 exception", DateTime? expiry = null) => new()
    {
        Name = name,
        BusinessJustification = "The vendor patch breaks the payment integration.",
        AuthorizingManagerId = 1,
        ExpiresAt = expiry ?? DateTime.UtcNow.AddDays(60)
    };

    // --- transitions ------------------------------------------------------------------------

    [Fact]
    public async Task TestTransitionWritesTheStatusAndItsHistoryTogether()
    {
        Seed(ctx => { ctx.Users.Add(NewUser(1, "analyst")); ctx.Vulnerabilities.Add(NewFinding(1)); });

        await _svc.TransitionAsync(1, FindingStatus.Verified, userId: 1, FindingStatusChangeSource.Manual);

        await using var db = OpenContext();
        Assert.Equal(FindingStatus.Verified, db.Vulnerabilities.Single().LifecycleStatus);

        var history = db.FindingStatusHistories.Single();
        Assert.Equal(FindingStatus.Active, history.FromStatus);
        Assert.Equal(FindingStatus.Verified, history.ToStatus);
        Assert.Equal(1, history.UserId);
        Assert.Equal(FindingStatusChangeSource.Manual, history.Source);
    }

    [Fact]
    public async Task TestIllegalTransitionIsRefusedAndChangesNothing()
    {
        Seed(ctx => ctx.Vulnerabilities.Add(NewFinding(1, FindingStatus.FalsePositive)));

        await Assert.ThrowsAsync<InvalidStateTransitionException>(() =>
            _svc.TransitionAsync(1, FindingStatus.Mitigated, 1, FindingStatusChangeSource.Manual, "why"));

        await using var db = OpenContext();
        Assert.Equal(FindingStatus.FalsePositive, db.Vulnerabilities.Single().LifecycleStatus);
        // A refused transition must not leave a history row claiming it happened.
        Assert.Empty(db.FindingStatusHistories);
    }

    [Fact]
    public async Task TestSuppressingTransitionRequiresAJustification()
    {
        Seed(ctx => ctx.Vulnerabilities.Add(NewFinding(1)));

        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.TransitionAsync(1, FindingStatus.FalsePositive, 1, FindingStatusChangeSource.Manual));
    }

    [Fact]
    public async Task TestDuplicateLinksTheCanonicalFindingAndClearsItOnReopen()
    {
        Seed(ctx =>
        {
            ctx.Vulnerabilities.Add(NewFinding(1));
            ctx.Vulnerabilities.Add(NewFinding(2));
        });

        await _svc.TransitionAsync(1, FindingStatus.Duplicate, 1, FindingStatusChangeSource.Manual,
            "same as #2", duplicateOfId: 2);

        await using (var db = OpenContext())
        {
            Assert.Equal(2, db.Vulnerabilities.Single(v => v.Id == 1).DuplicateOfId);
        }

        await _svc.TransitionAsync(1, FindingStatus.Active, 1, FindingStatusChangeSource.Manual);

        await using (var db = OpenContext())
        {
            // Leaving the link set would have the detail view keep showing a canonical finding this
            // one is no longer a duplicate of.
            Assert.Null(db.Vulnerabilities.Single(v => v.Id == 1).DuplicateOfId);
        }
    }

    [Fact]
    public async Task TestDuplicateOfAFindingThatDoesNotExistIsRefused()
    {
        Seed(ctx => ctx.Vulnerabilities.Add(NewFinding(1)));

        // A duplicate pointing at nothing is a finding that has been hidden.
        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            _svc.TransitionAsync(1, FindingStatus.Duplicate, 1, FindingStatusChangeSource.Manual,
                "same", duplicateOfId: 999));
    }

    [Fact]
    public async Task TestTransitionOnAMissingFindingIsNotFound()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            _svc.TransitionAsync(404, FindingStatus.Verified, 1, FindingStatusChangeSource.Manual));
    }

    [Fact]
    public async Task TestHistoryIsNewestFirst()
    {
        Seed(ctx => ctx.Vulnerabilities.Add(NewFinding(1)));

        await _svc.TransitionAsync(1, FindingStatus.Verified, 1, FindingStatusChangeSource.Manual);
        await _svc.TransitionAsync(1, FindingStatus.Mitigated, 1, FindingStatusChangeSource.Manual);

        var history = await _svc.GetHistoryAsync(1);

        Assert.Equal(2, history.Count);
        Assert.Equal(FindingStatus.Mitigated, history[0].ToStatus);
        Assert.Equal(FindingStatus.Verified, history[1].ToStatus);
    }

    [Fact]
    public async Task TestCreationEventCarriesNoFromStatus()
    {
        Seed(ctx => ctx.Vulnerabilities.Add(NewFinding(1)));

        await _svc.RecordCreationAsync(1, FindingStatus.Active, 1, FindingStatusChangeSource.Import);

        var history = await _svc.GetHistoryAsync(1);
        // Writing Active as the from-state would misrepresent creation as a transition.
        Assert.Null(Assert.Single(history).FromStatus);
    }

    [Fact]
    public async Task TestAllowedTransitionsComeFromTheMatrix()
    {
        Seed(ctx => ctx.Vulnerabilities.Add(NewFinding(1, FindingStatus.FalsePositive)));

        Assert.Equal([FindingStatus.Active], await _svc.GetAllowedTransitionsAsync(1));
    }

    // --- risk acceptance --------------------------------------------------------------------

    [Fact]
    public async Task TestCreatingAnAcceptanceSuppressesItsFindings()
    {
        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(1, "manager"));
            ctx.Vulnerabilities.Add(NewFinding(1));
            ctx.Vulnerabilities.Add(NewFinding(2));
        });

        var acceptance = await _svc.CreateAcceptanceAsync(NewAcceptance(), [1, 2], userId: 1);

        Assert.Equal(RiskAcceptanceStatus.Active, acceptance.Status);

        await using var db = OpenContext();
        Assert.All(db.Vulnerabilities.ToList(),
            v => Assert.Equal(FindingStatus.RiskAccepted, v.LifecycleStatus));

        // Each finding gets its own event naming the acceptance, so the timeline explains itself.
        Assert.Equal(2, db.FindingStatusHistories.Count(h => h.ToStatus == FindingStatus.RiskAccepted));
        Assert.All(db.FindingStatusHistories.ToList(), h => Assert.Equal(acceptance.Id, h.RiskAcceptanceId));
        Assert.Equal(2, db.RiskAcceptanceFindings.Count());
    }

    [Fact]
    public async Task TestAcceptanceRequiresAFutureExpiry()
    {
        Seed(ctx => ctx.Users.Add(NewUser(1, "manager")));

        var expired = NewAcceptance(expiry: DateTime.UtcNow.AddDays(-1));

        // Letting a past date through means the expiry job reactivates everything on its next run
        // with no explanation anybody can act on.
        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.CreateAcceptanceAsync(expired, [], 1));

        Assert.Equal(nameof(RiskAcceptance.ExpiresAt), ex.ParameterName);
    }

    [Fact]
    public async Task TestAcceptanceRequiresAJustificationAndAManager()
    {
        Seed(ctx => ctx.Users.Add(NewUser(1, "manager")));

        var noJustification = NewAcceptance();
        noJustification.BusinessJustification = "  ";
        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.CreateAcceptanceAsync(noJustification, [], 1));

        var noManager = NewAcceptance();
        noManager.AuthorizingManagerId = 0;
        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.CreateAcceptanceAsync(noManager, [], 1));
    }

    [Fact]
    public async Task TestAcceptingAFindingThatDoesNotExistIsRefusedWholesale()
    {
        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(1, "manager"));
            ctx.Vulnerabilities.Add(NewFinding(1));
        });

        // Reported rather than silently skipped: an acceptance covering less than the operator
        // believes is worse than one that fails to save.
        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            _svc.CreateAcceptanceAsync(NewAcceptance(), [1, 999], 1));
    }

    [Fact]
    public async Task TestRevokingReactivatesTheCoveredFindings()
    {
        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(1, "manager"));
            ctx.Vulnerabilities.Add(NewFinding(1));
        });

        var acceptance = await _svc.CreateAcceptanceAsync(NewAcceptance(), [1], 1);

        await _svc.RevokeAcceptanceAsync(acceptance.Id, "The compensating control was removed.", 1);

        await using var db = OpenContext();
        Assert.Equal(RiskAcceptanceStatus.Revoked, db.RiskAcceptances.Single().Status);
        Assert.Equal(FindingStatus.Active, db.Vulnerabilities.Single().LifecycleStatus);
        Assert.Contains(db.FindingStatusHistories.ToList(),
            h => h.ToStatus == FindingStatus.Active && h.Justification!.Contains("revoked"));
    }

    [Fact]
    public async Task TestRevokingRequiresAReason()
    {
        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(1, "manager"));
            ctx.Vulnerabilities.Add(NewFinding(1));
        });

        var acceptance = await _svc.CreateAcceptanceAsync(NewAcceptance(), [1], 1);

        // Revoking is as consequential as accepting.
        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.RevokeAcceptanceAsync(acceptance.Id, "   ", 1));
    }

    [Fact]
    public async Task TestARevokedAcceptanceCannotBeRevokedAgainOrEdited()
    {
        Seed(ctx => ctx.Users.Add(NewUser(1, "manager")));

        var acceptance = await _svc.CreateAcceptanceAsync(NewAcceptance(), [], 1);
        await _svc.RevokeAcceptanceAsync(acceptance.Id, "done", 1);

        await Assert.ThrowsAsync<InvalidStateTransitionException>(() =>
            _svc.RevokeAcceptanceAsync(acceptance.Id, "again", 1));

        acceptance.Name = "renamed";
        await Assert.ThrowsAsync<InvalidStateTransitionException>(() =>
            _svc.UpdateAcceptanceAsync(acceptance, 1));
    }

    [Fact]
    public async Task TestExpiringWithinFilterOnlyReturnsLiveAcceptances()
    {
        Seed(ctx => ctx.Users.Add(NewUser(1, "manager")));

        await _svc.CreateAcceptanceAsync(NewAcceptance("soon", DateTime.UtcNow.AddDays(10)), [], 1);
        await _svc.CreateAcceptanceAsync(NewAcceptance("later", DateTime.UtcNow.AddDays(200)), [], 1);

        var expiring = await _svc.GetAcceptancesAsync(expiringWithinDays: 30);

        Assert.Equal("soon", Assert.Single(expiring).Name);
        Assert.Equal(2, (await _svc.GetAcceptancesAsync()).Count);
    }

    // --- expiry job (3.2.4) -----------------------------------------------------------------

    [Fact]
    public async Task TestExpiryPassExpiresAndReactivatesExactlyOnce()
    {
        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(1, "manager"));
            ctx.Vulnerabilities.Add(NewFinding(1));
        });

        var acceptance = await _svc.CreateAcceptanceAsync(NewAcceptance(expiry: DateTime.UtcNow.AddDays(1)),
            [1], 1);

        // Wound forward past the expiry rather than seeding a past date, which creation refuses.
        Seed(ctx =>
        {
            var stored = ctx.RiskAcceptances.Single(a => a.Id == acceptance.Id);
            stored.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        });

        var first = await _svc.ProcessExpiredAcceptancesAsync(DateTime.UtcNow);

        Assert.Single(first.Expired);
        Assert.Equal([1], first.ReactivatedFindings[acceptance.Id]);

        await using (var db = OpenContext())
        {
            Assert.Equal(RiskAcceptanceStatus.Expired, db.RiskAcceptances.Single().Status);
            Assert.Equal(FindingStatus.Active, db.Vulnerabilities.Single().LifecycleStatus);
            Assert.Contains(db.FindingStatusHistories.ToList(),
                h => h.Source == FindingStatusChangeSource.Job && h.ToStatus == FindingStatus.Active);
        }

        // "Processed on the next run exactly once": a second pass has nothing to do.
        var second = await _svc.ProcessExpiredAcceptancesAsync(DateTime.UtcNow);
        Assert.Empty(second.Expired);
    }

    [Fact]
    public async Task TestExpiryPassLeavesRetriagedFindingsAlone()
    {
        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(1, "manager"));
            ctx.Vulnerabilities.Add(NewFinding(1));
            ctx.Vulnerabilities.Add(NewFinding(2));
        });

        var acceptance = await _svc.CreateAcceptanceAsync(NewAcceptance(expiry: DateTime.UtcNow.AddDays(1)),
            [1, 2], 1);

        // Someone decided in the meantime that finding 2 was never real.
        await _svc.TransitionAsync(2, FindingStatus.Active, 1, FindingStatusChangeSource.Manual);
        await _svc.TransitionAsync(2, FindingStatus.FalsePositive, 1, FindingStatusChangeSource.Manual, "not real");

        Seed(ctx => ctx.RiskAcceptances.Single(a => a.Id == acceptance.Id).ExpiresAt = DateTime.UtcNow.AddDays(-1));

        var result = await _svc.ProcessExpiredAcceptancesAsync(DateTime.UtcNow);

        // Only the still-accepted finding comes back; dragging the re-triaged one to Active would
        // overwrite a human decision with an automated one.
        Assert.Equal([1], result.ReactivatedFindings[acceptance.Id]);

        await using var db = OpenContext();
        Assert.Equal(FindingStatus.FalsePositive, db.Vulnerabilities.Single(v => v.Id == 2).LifecycleStatus);
    }

    [Fact]
    public async Task TestPreExpiryWarningFiresOncePerThreshold()
    {
        Seed(ctx => ctx.Users.Add(NewUser(1, "manager")));

        var acceptance = await _svc.CreateAcceptanceAsync(
            NewAcceptance(expiry: DateTime.UtcNow.AddDays(20)), [], 1);

        var first = await _svc.ProcessExpiredAcceptancesAsync(DateTime.UtcNow);
        var (warned, days) = Assert.Single(first.Warnings);
        Assert.Equal(acceptance.Id, warned.Id);
        Assert.Equal(30, days);

        // Re-running the same day must send nothing new — otherwise re-running a failed job becomes
        // something an operator has to think about.
        Assert.Empty((await _svc.ProcessExpiredAcceptancesAsync(DateTime.UtcNow)).Warnings);
    }

    [Fact]
    public async Task TestTighterThresholdWarnsAgain()
    {
        Seed(ctx => ctx.Users.Add(NewUser(1, "manager")));

        await _svc.CreateAcceptanceAsync(NewAcceptance(expiry: DateTime.UtcNow.AddDays(20)), [], 1);

        await _svc.ProcessExpiredAcceptancesAsync(DateTime.UtcNow);

        // Thirteen days later the acceptance is inside T-7, which is a new and more urgent message.
        var later = await _svc.ProcessExpiredAcceptancesAsync(DateTime.UtcNow.AddDays(14));

        Assert.Equal(7, Assert.Single(later.Warnings).DaysBefore);
    }

    [Fact]
    public async Task TestExtendingTheExpiryReArmsTheWarnings()
    {
        Seed(ctx => ctx.Users.Add(NewUser(1, "manager")));

        var acceptance = await _svc.CreateAcceptanceAsync(
            NewAcceptance(expiry: DateTime.UtcNow.AddDays(20)), [], 1);

        await _svc.ProcessExpiredAcceptancesAsync(DateTime.UtcNow);

        acceptance.ExpiresAt = DateTime.UtcNow.AddDays(25);
        await _svc.UpdateAcceptanceAsync(acceptance, 1);

        // The old "already warned" marker refers to a deadline that no longer applies; leaving it
        // set would silently skip the warnings for the new one.
        Assert.Single((await _svc.ProcessExpiredAcceptancesAsync(DateTime.UtcNow)).Warnings);
    }
}
