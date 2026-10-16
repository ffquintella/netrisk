using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.RiskIndex;
using ServerServices.Interfaces;
using ServerServices.RiskIndex.Read;
using ServerServices.Security;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// The committee's minutes on an index profile (S40 §7.3, §11.3; finding 3 of the M53 review), in the pattern of
/// <c>Track8/DeferredSecurityFixesInMemoryTest</c>: <c>risk_index_view</c>, global scope — administrators included — and the
/// shadow rule decide who downloads them, and the generic files routes never link one.
/// </summary>
[TestSubject(typeof(FileAccessAuthorizer))]
public class FileAccessAuthorizerRiskIndexTest : InMemoryServiceTestBase
{
    private const int Uploader = 1, Committee = 2, Viewer = 3, AuthorConfigurer = 4, OtherConfigurer = 5, NoView = 6, Root = 7;
    private const int ProfileId = 40, FileId = 41;

    private IFileAccessAuthorizer Authorizer => GetService<IFileAccessAuthorizer>();

    public FileAccessAuthorizerRiskIndexTest()
    {
        SeedUnscoped(ctx =>
        {
            var view = new Permission { Id = 1, Key = RiskIndexPermissions.View, Name = "v", Description = "" };
            var configure = new Permission { Id = 2, Key = RiskIndexPermissions.Configure, Name = "c", Description = "" };
            var approve = new Permission { Id = 3, Key = RiskIndexPermissions.Approve, Name = "a", Description = "" };
            ctx.Roles.AddRange(
                new Role { Value = 10, Name = "RiskManager", Permissions = [view, configure, approve] },
                new Role { Value = 11, Name = "Viewer", Permissions = [view] },
                new Role { Value = 12, Name = "RiskAnalyst", Permissions = [view, configure] },
                new Role { Value = 13, Name = "Plain" },
                new Role { Value = 14, Name = "Administrator", Admin = true });
            ctx.Users.AddRange(NewUser(Uploader, 10), NewUser(Committee, 10), NewUser(Viewer, 11), NewUser(AuthorConfigurer, 12),
                NewUser(OtherConfigurer, 12), NewUser(NoView, 13), NewUser(Root, 14, admin: true));
            ctx.RiskIndexProfiles.Add(new RiskIndexProfile
            {
                Id = ProfileId, Version = 1, Name = "Pilot", Mode = RiskIndexMode.Interim, Status = RiskIndexProfileStatus.PendingApproval,
                Parameters = "{}", Justification = "pilot", SubmittedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
                CreatedById = AuthorConfigurer, SubmittedById = AuthorConfigurer
            });
        });
    }

    private static User NewUser(int id, int role, bool admin = false) => new()
    {
        Value = id, Name = $"user {id}", Login = $"u{id}", Enabled = true, Admin = admin, RoleId = role, Type = "local", Salt = "s",
        Password = Encoding.UTF8.GetBytes("p"), Email = $"u{id}@x.test"
    };

    private static NrFile Minutes() => new()
    {
        Id = FileId, Name = "ata-crti.pdf", UniqueName = "ata-crti", User = Uploader, Content = [1, 2, 3], Size = 3,
        Timestamp = DateTime.UtcNow, RiskIndexProfileId = ProfileId
    };

    private User Reader(int id)
    {
        using var db = OpenContext();
        return db.Users.Single(u => u.Value == id);
    }

    private Task Read(int user) => Authorizer.EnsureCanReadAsync(Minutes(), Reader(user));

    private async Task<string> Refused(int user) => (await Assert.ThrowsAsync<UserNotAuthorizedException>(() => Read(user))).Destination;

    private void Publish() =>
        SeedUnscoped(ctx => ctx.Settings.Add(new Setting { Name = RiskIndexSettingKeys.Publication, Value = "published" }));

    [Fact]
    public async Task TestInShadowTheCommitteeAdministratorsAndTheAuthorsReadTheMinutes()
    {
        await Read(Uploader);
        await Read(Committee);
        await Read(Root);
        await Read(AuthorConfigurer);

        Assert.Equal(RiskIndexErrors.InShadow, await Refused(Viewer));
        Assert.Equal(RiskIndexErrors.InShadow, await Refused(OtherConfigurer));
    }

    [Fact]
    public async Task TestOncePublishedEveryViewerReadsTheMinutesAndNobodyElse()
    {
        Publish();

        await Read(Viewer);
        await Read(OtherConfigurer);

        Assert.Equal(RiskIndexPermissions.View, await Refused(NoView));
    }

    /// <summary>The minutes are read with global scope, as every value of a profile (S40 §8.4) — the administrator too.</summary>
    [Fact]
    public async Task TestAScopedReaderIsRefusedTheMinutesEvenAsAdministrator()
    {
        Publish();
        ScopeTo(500);

        Assert.Equal(RiskIndexErrors.ProfileValuesRequireGlobalScope, await Refused(Committee));
        Assert.Equal(RiskIndexErrors.ProfileValuesRequireGlobalScope, await Refused(Root));
        await Read(Uploader); // reading back one's own upload is the upload-then-attach flow
    }

    /// <summary>Linking goes through the profile's own route; the generic files routes refuse it — administrators included.</summary>
    [Fact]
    public async Task TestTheFilesRoutesNeverLinkMinutesToAProfile()
    {
        var body = Minutes();
        body.User = Committee;

        await Assert.ThrowsAsync<InvalidParameterException>(() => Authorizer.EnsureCanAttachAsync(body, Reader(Committee)));
        await Assert.ThrowsAsync<InvalidParameterException>(() => Authorizer.EnsureCanAttachAsync(body, Reader(Root)));
        Assert.Contains(FileParents.Declared(body), p => p.Kind == FileParents.Kind.RiskIndexProfile && p.Id == ProfileId);
    }
}
