using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL;
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
/// The files write path (security findings NR-2026-034 and NR-2026-035).
///
/// NR-2026-034: <c>PUT /Files/{name}</c> authorized against the owner <em>the request body claimed</em>
/// and then copied the whole body onto the stored row, so knowing a file's id and unique name was enough
/// to replace its content, take its ownership, re-parent it and re-stamp its entity.
///
/// NR-2026-035: <c>POST /Files</c> and <c>POST /Files/local/complete</c> attached a file to whatever
/// parent the body named, with whatever <c>entity_id</c> it carried, without asking whether the caller
/// could write to that parent.
///
/// As in <see cref="DeferredSecurityFixesInMemoryTest"/>, every rule is asserted from the refusing side —
/// the pre-fix behaviour was "allow", so an allowed-case test alone would have passed before the fix —
/// and the allowed cases that must survive are asserted next to them.
/// </summary>
[TestSubject(typeof(FileAccessAuthorizer))]
public class FileWriteAuthorizationInMemoryTest : InMemoryServiceTestBase
{
    private const int EntityA = 3;
    private const int EntityB = 4;

    private static User NewUser(int id, string name, bool admin = false) => new()
    {
        Value = id, Name = name, Login = name, Enabled = true, Admin = admin,
        Type = "local", Salt = "s", Password = Encoding.UTF8.GetBytes("p"), Email = $"{name}@x.test"
    };

    private static readonly User Alice = NewUser(1, "alice");
    private static readonly User Mallory = NewUser(2, "mallory");
    private static readonly User RiskOwner = NewUser(3, "riskowner");
    private static readonly User Root = NewUser(9, "root", admin: true);

    private IFileAccessAuthorizer Authorizer => GetService<IFileAccessAuthorizer>();

    private FilesService Files() => new(Serilog.Log.Logger, GetService<IDalService>());

    private static Risk NewRisk(int id, int entityId, int? owner = null) => new()
    {
        Id = id, Status = "New", Subject = $"Risk {id}", ReferenceId = $"R-{id}",
        Assessment = string.Empty, Notes = string.Empty,
        RiskCatalogMapping = string.Empty, ThreatCatalogMapping = string.Empty,
        SubmissionDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        LastUpdate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        EntityId = entityId, Owner = owner
    };

    private static Permission NewPermission(int id, string key) =>
        new() { Id = id, Key = key, Name = key, Description = string.Empty, Order = id };

    /// <summary>
    /// Alice holds <paramref name="alicePermissions"/>; Mallory holds nothing; the risk owner holds nothing
    /// but owns risk 5. Risk 5 belongs to entity A, risk 6 to entity B; mitigation 7 hangs off risk 5;
    /// incident 8 belongs to entity A; IRP 11 has no entity; acceptance 12 belongs to entity A.
    /// </summary>
    private void SeedWorld(params string[] alicePermissions)
    {
        Seed(ctx =>
        {
            ctx.FileTypes.Add(new FileType { Value = 1, Name = "text/plain" });

            var alice = NewUser(1, "alice");
            var id = 1;
            foreach (var key in alicePermissions)
                alice.Permissions.Add(NewPermission(id++, key));
            ctx.Users.Add(alice);
            ctx.Users.Add(NewUser(2, "mallory"));
            ctx.Users.Add(NewUser(3, "riskowner"));
            ctx.Users.Add(NewUser(9, "root", admin: true));

            ctx.Risks.Add(NewRisk(5, EntityA, owner: 3));
            ctx.Risks.Add(NewRisk(6, EntityB));
            ctx.Mitigations.Add(new Mitigation
            {
                Id = 7, RiskId = 5, PlanningStrategy = 1, MitigationEffort = 1, MitigationCost = 1,
                MitigationOwner = 3, SubmittedBy = 3, MitigationPercent = 0,
                CurrentSolution = string.Empty, SecurityRequirements = string.Empty,
                SecurityRecommendations = string.Empty,
                SubmissionDate = DateTime.UtcNow, LastUpdate = DateTime.UtcNow,
                PlanningDate = new DateOnly(2026, 6, 1)
            });
            ctx.Incidents.Add(new Incident { Id = 8, Description = "outage", CreatedById = 1, EntityId = EntityA });
            ctx.IncidentResponsePlans.Add(new IncidentResponsePlan
            {
                Id = 11, Name = "IRP", Description = "plan", CreatedById = 1,
                CreationDate = DateTime.UtcNow, LastUpdate = DateTime.UtcNow
            });
            ctx.RiskAcceptances.Add(new RiskAcceptance
            {
                Id = 12, Name = "Acceptance", AuthorizingManagerId = 1, EntityId = EntityA,
                StartDate = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(30), CreatedAt = DateTime.UtcNow
            });
        });
    }

    private static NrFile Upload(Action<NrFile>? parent = null)
    {
        var file = new NrFile { Name = "report.txt", Type = "1", Content = [1, 2, 3], UniqueName = string.Empty };
        parent?.Invoke(file);
        return file;
    }

    // --- NR-2026-035: the write-side authorizer --------------------------------------------------------

    /// <summary>The finding in one test: a caller who cannot touch the risk cannot attach to it.</summary>
    [Fact]
    public async Task TestAttachingToARiskIsRefusedWithoutTheRiskPermission()
    {
        SeedWorld();

        await Assert.ThrowsAsync<UserNotAuthorizedException>(() =>
            Authorizer.EnsureCanAttachAsync(Upload(f => f.RiskId = 5), Mallory));
    }

    [Fact]
    public async Task TestAttachingToARiskIsAllowedWithTheRiskPermission()
    {
        SeedWorld(FileAccessAuthorizer.RiskPermission);

        await Authorizer.EnsureCanAttachAsync(Upload(f => f.RiskId = 5), Alice);
    }

    /// <summary>The register's relationship rule carries over: the risk owner edits the risk, so may attach.</summary>
    [Fact]
    public async Task TestTheRiskOwnerMayAttachWithoutTheBlanketPermission()
    {
        SeedWorld();

        await Authorizer.EnsureCanAttachAsync(Upload(f => f.RiskId = 5), RiskOwner);
        await Authorizer.EnsureCanAttachAsync(Upload(f => f.MitigationId = 7), RiskOwner);
    }

    [Fact]
    public async Task TestAttachingToAMitigationInheritsTheRisksRules()
    {
        SeedWorld();

        await Assert.ThrowsAsync<UserNotAuthorizedException>(() =>
            Authorizer.EnsureCanAttachAsync(Upload(f => f.MitigationId = 7), Mallory));
    }

    /// <summary>
    /// The permission is not enough: the parent has to be inside the caller's entity scope. Before the fix
    /// this was the cross-tenant plant — the file then showed up in the other entity's risk.
    /// </summary>
    [Fact]
    public async Task TestAttachingToARiskOutsideTheCallersScopeIsRefusedEvenWithThePermission()
    {
        SeedWorld(FileAccessAuthorizer.RiskPermission);
        ScopeTo(EntityA);

        await Authorizer.EnsureCanAttachAsync(Upload(f => f.RiskId = 5), Alice);
        await Assert.ThrowsAsync<UserNotAuthorizedException>(() =>
            Authorizer.EnsureCanAttachAsync(Upload(f => f.RiskId = 6), Alice));
    }

    /// <summary>A dangling FK is not a permission question, so not even an administrator passes it.</summary>
    [Fact]
    public async Task TestAttachingToAParentThatDoesNotExistIsRefusedEvenForAnAdministrator()
    {
        SeedWorld(FileAccessAuthorizer.RiskPermission);

        await Assert.ThrowsAsync<UserNotAuthorizedException>(() =>
            Authorizer.EnsureCanAttachAsync(Upload(f => f.RiskId = 404), Alice));
        await Assert.ThrowsAsync<UserNotAuthorizedException>(() =>
            Authorizer.EnsureCanAttachAsync(Upload(f => f.IncidentResponsePlanTaskId = 404), Root));
    }

    [Fact]
    public async Task TestAnAdministratorMayAttachToAnyVisibleParent()
    {
        SeedWorld();

        await Authorizer.EnsureCanAttachAsync(Upload(f => f.IncidentId = 8), Root);
        await Authorizer.EnsureCanAttachAsync(Upload(f => f.RiskAcceptanceId = 12), Root);
    }

    /// <summary>
    /// Two parents is how a caller allowed on one record would hang the file off another: only one of them
    /// would be checked.
    /// </summary>
    [Fact]
    public async Task TestAFileNamingTwoParentsIsRefused()
    {
        SeedWorld(FileAccessAuthorizer.RiskPermission);

        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Authorizer.EnsureCanAttachAsync(Upload(f => { f.RiskId = 5; f.IncidentId = 8; }), Alice));
    }

    /// <summary>A parentless upload is readable only by its uploader, so it reaches nobody else's record.</summary>
    [Fact]
    public async Task TestAParentlessUploadIsAllowedForAnyone()
    {
        SeedWorld();

        await Authorizer.EnsureCanAttachAsync(Upload(), Mallory);
    }

    [Theory]
    [InlineData("incident", FileAccessAuthorizer.IncidentPermission)]
    [InlineData("irp", FileAccessAuthorizer.IncidentResponsePlanPermission)]
    [InlineData("acceptance", FileAccessAuthorizer.AcceptanceWritePermission)]
    public async Task TestEachParentRequiresItsOwnPermission(string parent, string permission)
    {
        SeedWorld(permission);

        Action<NrFile> setParent = parent switch
        {
            "incident" => f => f.IncidentId = 8,
            "irp" => f => f.IncidentResponsePlanId = 11,
            _ => f => f.RiskAcceptanceId = 12
        };

        await Authorizer.EnsureCanAttachAsync(Upload(setParent), Alice);
        await Assert.ThrowsAsync<UserNotAuthorizedException>(() =>
            Authorizer.EnsureCanAttachAsync(Upload(setParent), Mallory));
    }

    /// <summary>
    /// Adding to an acceptance is changing it, so the read permission (<c>vulnerabilities</c>) is not
    /// enough — the acceptance routes' own write permission is.
    /// </summary>
    [Fact]
    public async Task TestTheAcceptanceReadPermissionDoesNotGrantAttaching()
    {
        SeedWorld(FileAccessAuthorizer.AcceptancePermission);

        await Assert.ThrowsAsync<UserNotAuthorizedException>(() =>
            Authorizer.EnsureCanAttachAsync(Upload(f => f.RiskAcceptanceId = 12), Alice));
    }

    // --- NR-2026-035: what Create takes from the body ---------------------------------------------------

    /// <summary>
    /// The entity is always derived from the parent. Before the fix it was <c>??=</c>, so an
    /// <c>entity_id</c> in the body won and filed the attachment under any entity the caller named.
    /// </summary>
    [Fact]
    public void TestCreateIgnoresTheEntityInTheBodyAndDerivesItFromTheParent()
    {
        SeedWorld(FileAccessAuthorizer.RiskPermission);

        var listing = Files().Create(Upload(f => { f.RiskId = 5; f.EntityId = EntityB; }), Alice);

        using var db = OpenContext();
        Assert.Equal(EntityA, db.NrFiles.Single(f => f.UniqueName == listing.UniqueName).EntityId);
    }

    /// <summary>Owner, id, unique name and size are the server's; the body cannot choose any of them.</summary>
    [Fact]
    public void TestCreateIgnoresTheServerOwnedFieldsInTheBody()
    {
        SeedWorld(FileAccessAuthorizer.RiskPermission);

        var listing = Files().Create(Upload(f =>
        {
            f.RiskId = 5;
            f.Id = 777;
            f.User = Mallory.Value;
            f.UniqueName = "chosen-by-the-client";
            f.Size = 1_000_000;
        }), Alice);

        using var db = OpenContext();
        var stored = db.NrFiles.Single();
        Assert.NotEqual(777, stored.Id);
        Assert.Equal(Alice.Value, stored.User);
        Assert.Equal(listing.UniqueName, stored.UniqueName);
        Assert.NotEqual("chosen-by-the-client", stored.UniqueName);
        Assert.Equal(3, stored.Size);
    }

    /// <summary>
    /// The body is a whole NrFile, navigations included. Adding it as-is inserted whatever graph it carried
    /// — here a new business entity, which also overrode the derived entity_id.
    /// </summary>
    [Fact]
    public void TestCreateDoesNotInsertANavigationGraphFromTheBody()
    {
        SeedWorld(FileAccessAuthorizer.RiskPermission);

        Files().Create(Upload(f =>
        {
            f.RiskId = 5;
            f.Entity = new Entity
            {
                Id = 99, DefinitionName = "planted", DefinitionVersion = "1", Status = "active",
                Created = DateTime.UtcNow, Updated = DateTime.UtcNow, CreatedBy = 2, UpdatedBy = 2
            };
        }), Alice);

        using var db = OpenContext();
        Assert.DoesNotContain(db.Entities, e => e.Id == 99);
        Assert.Equal(EntityA, db.NrFiles.Single().EntityId);
    }

    [Fact]
    public void TestCreateRefusesAFileNamingTwoParents()
    {
        SeedWorld(FileAccessAuthorizer.RiskPermission);

        Assert.Throws<InvalidParameterException>(() =>
            Files().Create(Upload(f => { f.RiskId = 5; f.IncidentId = 8; }), Alice));

        using var db = OpenContext();
        Assert.Empty(db.NrFiles);
    }

    [Fact]
    public void TestCreateRefusesABlankName()
    {
        SeedWorld();

        var thrown = Assert.Throws<InvalidParameterException>(() =>
            Files().Create(Upload(f => f.Name = "  "), Alice));

        Assert.Equal("name", thrown.ParameterName);
    }

    /// <summary>
    /// The type used to be looked up after the row was saved, so an unknown one stored the file and then
    /// failed with a NullReferenceException (a 500). It is now refused first and nothing is stored.
    /// </summary>
    [Theory]
    [InlineData("999")]
    [InlineData("not-a-number")]
    [InlineData("")]
    [InlineData(null)]
    public void TestCreateRefusesAnUnknownFileTypeAndStoresNothing(string? type)
    {
        SeedWorld();

        var thrown = Assert.Throws<InvalidParameterException>(() =>
            Files().Create(Upload(f => f.Type = type!), Alice));

        Assert.Equal("type", thrown.ParameterName);
        using var db = OpenContext();
        Assert.Empty(db.NrFiles);
    }

    [Fact]
    public void TestCreateReturnsTheNameOfAKnownFileType()
    {
        SeedWorld();

        var listing = Files().Create(Upload(), Alice);

        Assert.Equal("text/plain", listing.Type);
    }

    // --- NR-2026-034: Save -----------------------------------------------------------------------------

    /// <summary>A stored risk attachment owned by Alice, in entity A, with known content.</summary>
    private void SeedStoredFile(int owner = 1)
    {
        Seed(ctx => ctx.NrFiles.Add(new NrFile
        {
            Id = 20, Name = "original.txt", UniqueName = "u-20", Type = "1", Size = 3, User = owner,
            Content = [1, 2, 3], Timestamp = new DateTime(2026, 1, 1), RiskId = 5, EntityId = EntityA
        }));
    }

    /// <summary>The body every tampering test sends: it renames, and it also tries to change everything else.</summary>
    private static NrFile TamperedBody(int claimedOwner) => new()
    {
        Id = 20, UniqueName = "u-20", Name = "renamed.txt", Type = "1", Size = 2, User = claimedOwner,
        Content = [9, 9], Timestamp = new DateTime(2030, 1, 1),
        RiskId = 6, IncidentId = 8, MitigationId = 7, EntityId = EntityB
    };

    private static void AssertOnlyTheNameMayHaveChanged(NrFile stored, string expectedName, int owner = 1)
    {
        Assert.Equal(expectedName, stored.Name);
        Assert.Equal(new byte[] { 1, 2, 3 }, stored.Content);
        Assert.Equal(3, stored.Size);
        Assert.Equal(owner, stored.User);
        Assert.Equal(5, stored.RiskId);
        Assert.Null(stored.IncidentId);
        Assert.Null(stored.MitigationId);
        Assert.Equal(EntityA, stored.EntityId);
        Assert.Equal(new DateTime(2026, 1, 1), stored.Timestamp);
    }

    /// <summary>The finding: a stranger who claims ownership in the body is refused, and nothing changes.</summary>
    [Fact]
    public void TestANonOwnerCannotSaveAnotherUsersFileByClaimingOwnershipInTheBody()
    {
        SeedWorld();
        SeedStoredFile(owner: Alice.Value);

        Assert.Throws<UserNotAuthorizedException>(() =>
            Files().Save(TamperedBody(claimedOwner: Mallory.Value), Mallory));

        using var db = OpenContext();
        AssertOnlyTheNameMayHaveChanged(db.NrFiles.Single(), "original.txt");
    }

    /// <summary>The owner may rename; the rest of the body is ignored rather than written.</summary>
    [Fact]
    public void TestTheOwnerCanRenameButNothingElse()
    {
        SeedWorld();
        SeedStoredFile(owner: Alice.Value);

        Files().Save(TamperedBody(claimedOwner: Mallory.Value), Alice);

        using var db = OpenContext();
        AssertOnlyTheNameMayHaveChanged(db.NrFiles.Single(), "renamed.txt");
    }

    [Fact]
    public void TestAnAdministratorCanRenameAnyFile()
    {
        SeedWorld();
        SeedStoredFile(owner: Alice.Value);

        Files().Save(TamperedBody(claimedOwner: Root.Value), Root);

        using var db = OpenContext();
        AssertOnlyTheNameMayHaveChanged(db.NrFiles.Single(), "renamed.txt");
    }

    /// <summary>
    /// The unique name is the capability: a known id with the wrong unique name is "not found", not an
    /// error that confirms the id exists.
    /// </summary>
    [Fact]
    public void TestAMismatchedUniqueNameIsNotFound()
    {
        SeedWorld();
        SeedStoredFile(owner: Alice.Value);

        var body = TamperedBody(claimedOwner: Alice.Value);
        body.UniqueName = "u-guessed";

        Assert.Throws<DataNotFoundException>(() => Files().Save(body, Alice));
    }

    [Fact]
    public void TestABlankRenameIsRefused()
    {
        SeedWorld();
        SeedStoredFile(owner: Alice.Value);

        var body = TamperedBody(claimedOwner: Alice.Value);
        body.Name = "";

        Assert.Throws<InvalidParameterException>(() => Files().Save(body, Alice));
    }
}
