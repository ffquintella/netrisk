using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.RiskIndex;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// The M53 review's evidence tables on a real MariaDB (S40 §6.4, §7.3, §11.4): the permission-grant record keeps the real
/// ids of a role and a user created in the same save as their grant — the provider's temporary keys are what the in-memory
/// tests cannot show — survives their deletion with its author, and is refused an update; a deleted draft takes its editor
/// rows, and a profile's minutes lose the link, not the file. Needs Docker.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class RiskIndexApprovalEvidenceIntegrationTests(MariaDbContainerFixture fixture)
{
    private const int Actor = 4242;

    private static User NewUser(string login, Role role) => new()
    {
        Name = login, Login = login, Enabled = true, Type = "local", Salt = "s", Password = "p"u8.ToArray(),
        Email = login + "@example.invalid", Role = role
    };

    [Fact]
    public async Task TestQ1_TheGrantRecordKeepsRealIdsOutlivesItsSubjectsAndIsInsertOnly()
    {
        Assert.True(MariaDbContainerFixture.VersionIntroducing("CREATE TABLE IF NOT EXISTS `permission_grant_events`") > 0);
        await fixture.InitializeNumberedSchemaAsync(MariaDbContainerFixture.TargetSchemaVersion);

        int roleId, userId;
        await using (var db = fixture.NewContext())
        {
            db.UserId = Actor;
            db.AuditActor = "u" + Actor;
            var approve = await db.Permissions.SingleAsync(p => p.Key == RiskIndexPermissions.Approve);
            var role = new Role { Name = "Committee-" + Guid.NewGuid().ToString("N")[..8], Permissions = [approve] };
            var user = NewUser("c" + Guid.NewGuid().ToString("N")[..8], role);
            db.Roles.Add(role);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            (roleId, userId) = (role.Value, user.Value);
        }

        await using (var db = fixture.NewContext())
        {
            var events = await db.PermissionGrantEvents.AsNoTracking().Where(e => e.RoleId == roleId).ToListAsync();
            Assert.Contains(events, e => e is { Kind: PermissionGrantKind.RolePermission, Action: PermissionGrantAction.Grant, ActorId: Actor }
                                         && e.PermissionKey == RiskIndexPermissions.Approve);
            Assert.Contains(events, e => e.Kind == PermissionGrantKind.RoleMembership && e.UserId == userId && e.ActorId == Actor);
            Assert.Contains(events, e => e.Kind == PermissionGrantKind.RoleName && e.ActorId == Actor);

            var one = events[0];
            var tracked = await db.PermissionGrantEvents.SingleAsync(e => e.Id == one.Id);
            tracked.ActorId = 1;
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }

        await using (var db = fixture.NewContext())
        {
            db.Users.Remove(await db.Users.SingleAsync(u => u.Value == userId));
            await db.SaveChangesAsync();
            db.Roles.Remove(await db.Roles.SingleAsync(r => r.Value == roleId));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.NewContext())
        {
            var kept = await db.PermissionGrantEvents.AsNoTracking()
                .Where(e => e.ActorId == Actor && e.RoleName != null && e.RoleName.StartsWith("Committee-")).ToListAsync();
            Assert.NotEmpty(kept);
            Assert.All(kept, e => Assert.Null(e.RoleId));
        }
    }

    [Fact]
    public async Task TestQ2_AProfileTakesItsEditorsAndLeavesItsMinutesUnlinked()
    {
        await fixture.InitializeNumberedSchemaAsync(MariaDbContainerFixture.TargetSchemaVersion);

        await using var db = fixture.NewContext();
        var profile = new RiskIndexProfile
        {
            Version = 9001 + Random.Shared.Next(1000), Name = "draft", Mode = RiskIndexMode.Interim, Status = RiskIndexProfileStatus.Draft,
            Parameters = "{}", CreatedAt = DateTime.UtcNow
        };
        db.RiskIndexProfiles.Add(profile);
        db.RiskIndexProfileEditors.Add(new RiskIndexProfileEditor { Profile = profile, CreatedAt = DateTime.UtcNow });
        var file = new NrFile
        {
            Name = "ata.pdf", UniqueName = "ata-" + Guid.NewGuid().ToString("N"), Type = "1", Size = 1, Timestamp = DateTime.UtcNow,
            User = 1, Content = [1]
        };
        db.NrFiles.Add(file);
        await db.SaveChangesAsync();
        file.RiskIndexProfileId = profile.Id;
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlRawAsync("DELETE FROM risk_index_profiles WHERE id = {0}", profile.Id);

        Assert.False(await db.RiskIndexProfileEditors.AsNoTracking().AnyAsync(e => e.ProfileId == profile.Id));
        Assert.Null((await db.NrFiles.AsNoTracking().SingleAsync(f => f.Id == file.Id)).RiskIndexProfileId);
    }
}
