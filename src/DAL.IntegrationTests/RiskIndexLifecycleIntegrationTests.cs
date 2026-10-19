using System.Globalization;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Model.RiskIndex;
using MySqlConnector;
using NSubstitute;
using ServerServices.RiskIndex;
using ServerServices.RiskIndex.Profiles;
using ServerServices.RiskIndex.Read;
using ServerServices.Governance;
using ServerServices.Security;
using ServerServices.Services;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// M53, T250 on a real MariaDB (S40 §7.2, §9.6, §12.7): the version adds the bridges and the preview requests and converges
/// on a replay; deleting a draft takes its previews (CASCADE) while a bridge keeps both its profiles (RESTRICT); a bridge
/// row is unique per new profile, node and category and filtered by its node's entity; and the preview job's claim is a
/// set-based update that only one of two runs wins. The version is found by its marker. Needs Docker.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class RiskIndexLifecycleIntegrationTests(MariaDbContainerFixture fixture)
{
    private static int V => MariaDbContainerFixture.VersionIntroducing("CREATE TABLE IF NOT EXISTS `risk_index_bridges`");

    private const int Unit = 9601, Other = 9602;

    private async Task<MySqlConnection> OpenAsync()
    {
        var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    private static async Task<long> CountAsync(MySqlConnection conn, string sql)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static Task ExecAsync(MySqlConnection conn, string sql) => MariaDbContainerFixture.ExecAsync(conn, sql);

    private static string Profile(int id, int version, int status) => string.Format(CultureInfo.InvariantCulture,
        "INSERT INTO risk_index_profiles (id, version, name, mode, status, parameters, justification, submitted_at, approved_at, " +
        "committee_decision_ref, effective_from, retired_at, created_at) " +
        "VALUES ({0}, {1}, 'v{1}', 1, {2}, '{{}}', {3}, {4}, {5}, {6}, {7}, {8}, '2026-09-01');",
        id, version, status,
        status == 1 ? "NULL" : "'why'", status == 1 ? "NULL" : "'2026-09-02'", status is 3 or 4 ? "'2026-09-03'" : "NULL",
        status is 3 or 4 ? "'CRTI'" : "NULL", status is 3 or 4 ? "'2026-09-04 05:00:00'" : "NULL",
        status == 4 ? "'2026-10-05 05:00:00'" : "NULL");

    private static string Bridge(int from, int to, string scopeRef, int? entity, int category = 0) => string.Format(
        CultureInfo.InvariantCulture,
        "INSERT INTO risk_index_bridges (from_profile_id, to_profile_id, bridge_date, scope_kind, scope_ref, entity_id, category, " +
        "value_old, value_new, overlay, overlay_partial, created_at) VALUES ({0}, {1}, '2026-10-05', {2}, '{3}', {4}, {5}, 61, 64, '[]', 0, NOW());",
        from, to, entity is null ? 1 : 2, scopeRef, entity?.ToString(CultureInfo.InvariantCulture) ?? "NULL", category);

    [Fact]
    public async Task TestQ1_TheVersionAddsTheTwoTablesAndConvergesOnAReplay()
    {
        await fixture.InitializeNumberedSchemaAsync(V - 1);
        await using var conn = await OpenAsync();
        var dir = MariaDbContainerFixture.RepoDbDir();

        var structure = await File.ReadAllTextAsync(Path.Combine(dir, "Structure", $"{V}.sql"));
        await ExecAsync(conn, structure);
        await ExecAsync(conn, structure);
        await ExecAsync(conn, await File.ReadAllTextAsync(Path.Combine(dir, "Data", $"{V}.sql")));
        await ExecAsync(conn, await File.ReadAllTextAsync(Path.Combine(dir, "Data", $"{V}.sql")));

        foreach (var table in new[] { "risk_index_bridges", "risk_index_preview_requests" })
            Assert.Equal(1, await CountAsync(conn,
                $"SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{table}'"));
        foreach (var fk in new[] { "fk_risk_index_preview_requests_profile_id", "fk_risk_index_bridges_to_profile_id" })
            Assert.Equal(1, await CountAsync(conn,
                $"SELECT COUNT(*) FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = '{fk}'"));
        Assert.Equal(V.ToString(CultureInfo.InvariantCulture),
            await new MySqlCommand("SELECT value FROM settings WHERE name = 'db_version'", conn).ExecuteScalarAsync() as string);
    }

    [Fact]
    public async Task TestQ2_ADraftTakesItsPreviewsAndABridgeKeepsItsProfiles()
    {
        await fixture.InitializeNumberedSchemaAsync(MariaDbContainerFixture.TargetSchemaVersion);
        await using var conn = await OpenAsync();
        await ExecAsync(conn, Profile(1, 1, 4) + Profile(2, 2, 3) + Profile(3, 3, 1));
        await ExecAsync(conn, "INSERT INTO risk_index_preview_requests (profile_id, parameters_hash, status, progress, created_at) " +
                              "VALUES (3, REPEAT('A', 64), 3, 100, NOW()), (3, REPEAT('B', 64), 1, 0, NOW());");
        await ExecAsync(conn, Bridge(1, 2, "org", null));

        await ExecAsync(conn, "DELETE FROM risk_index_profiles WHERE id = 3;");
        Assert.Equal(0, await CountAsync(conn, "SELECT COUNT(*) FROM risk_index_preview_requests"));

        await Assert.ThrowsAsync<MySqlException>(() => ExecAsync(conn, "DELETE FROM risk_index_profiles WHERE id = 1;"));
        await Assert.ThrowsAsync<MySqlException>(() => ExecAsync(conn, Bridge(1, 2, "org", null)));
        await Assert.ThrowsAsync<MySqlException>(() => ExecAsync(conn, Bridge(2, 2, "org", null, 1)));
        await Assert.ThrowsAsync<MySqlException>(() => ExecAsync(conn,
            "INSERT INTO risk_index_preview_requests (profile_id, parameters_hash, status, progress, created_at) " +
            "VALUES (2, REPEAT('A', 64), 9, 0, NOW());"));
    }

    [Fact]
    public async Task TestQ3_AScopedContextSeesOnlyItsOwnBridgeRows()
    {
        await fixture.InitializeNumberedSchemaAsync(MariaDbContainerFixture.TargetSchemaVersion);
        await using (var conn = await OpenAsync())
        {
            await ExecAsync(conn, Profile(1, 1, 4) + Profile(2, 2, 3));
            await ExecAsync(conn, Bridge(1, 2, "org", null) + Bridge(1, 2, Unit.ToString(CultureInfo.InvariantCulture), Unit) +
                                  Bridge(1, 2, Other.ToString(CultureInfo.InvariantCulture), Other));
        }

        await using (var scoped = fixture.NewScopedContext(Unit))
            Assert.Equal(new int?[] { Unit }, await scoped.RiskIndexBridges.Select(b => b.EntityId).ToListAsync());
        await using var unrestricted = fixture.NewContext();
        Assert.Equal(3, await unrestricted.RiskIndexBridges.CountAsync());
    }

    /// <summary>S40 §9.6: the claim is <c>UPDATE … WHERE status = Queued</c>; of two runs, one wins.</summary>
    [Fact]
    public async Task TestQ4_OnlyOneRunClaimsAPreviewRequest()
    {
        await fixture.InitializeNumberedSchemaAsync(MariaDbContainerFixture.TargetSchemaVersion);
        await using (var conn = await OpenAsync())
        {
            await ExecAsync(conn, Profile(3, 3, 1));
            await ExecAsync(conn, "INSERT INTO risk_index_preview_requests (id, profile_id, parameters_hash, status, progress, created_at) " +
                                  "VALUES (77, 3, REPEAT('A', 64), 1, 0, NOW());");
        }

        var dal = new DalService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = fixture.ConnectionString + "ConvertZeroDateTime=True;"
        }).Build(), BackgroundServiceHttpContextAccessor.WithUnrestrictedEntityScope());
        var service = new RiskIndexPreviewService(dal, Substitute.For<Serilog.ILogger>());
        var now = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

        var claims = await Task.WhenAll(service.ClaimAsync(77, now, default), service.ClaimAsync(77, now, default));

        Assert.Single(claims, c => c);
        await using var db = fixture.NewContext();
        Assert.Equal(RiskIndexPreviewStatus.Running, (await db.RiskIndexPreviewRequests.SingleAsync(r => r.Id == 77)).Status);
    }

    /// <summary>
    /// T249 on MariaDB: every read of the dashboard translates and answers over a real snapshot written by the production
    /// data access — the in-memory provider of the unit tests accepts queries MySQL would refuse.
    /// </summary>
    [Fact]
    public async Task TestQ5_TheReadApiTranslatesOverARealSnapshot()
    {
        await fixture.InitializeNumberedSchemaAsync(MariaDbContainerFixture.TargetSchemaVersion);
        var now = new DateTime(2026, 10, 5, 5, 0, 0, DateTimeKind.Utc);
        await using (var db = fixture.NewContext())
        {
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 0; SET SESSION sql_mode = '';");
            db.Entities.Add(new Entity
            {
                Id = Unit, DefinitionName = "organizationUnit", DefinitionVersion = "2.7", Status = "active",
                Created = now.AddYears(-1), Updated = now.AddYears(-1), CreatedBy = 1, UpdatedBy = 1
            });
            db.Hosts.Add(new Host
            {
                Id = 9701, HostName = "srv-01", Source = "test", Status = 1, EntityId = Unit, Criticality = 4,
                RegistrationDate = now.AddDays(-100)
            });
            db.Vulnerabilities.Add(new Vulnerability
            {
                Id = 990101, HostId = 9701, ImportSource = "nessus", Cvss3BaseScore = 8.1f, Cves = "CVE-2026-0101",
                LifecycleStatus = FindingStatus.Active, FirstDetection = now.AddDays(-30), LastDetection = now.AddDays(-1),
                Title = "finding", Status = 1, DetectionCount = 1,
                SlaDueDate = DateTime.UtcNow.AddDays(30) // the indicators are live counts, measured to the present (S40 §5.2.5)
            });
            db.Risks.Add(new Risk
            {
                Id = 9702, EntityId = Unit, Status = "New", Subject = "Leak", ReferenceId = "R-9702", Assessment = "", Notes = "",
                RiskCatalogMapping = "", ThreatCatalogMapping = "", SubmissionDate = new DateTime(2026, 1, 10),
                LastUpdate = new DateTime(2026, 1, 10), Owner = 1, SubmittedBy = 1
            });
            db.RiskScorings.Add(new RiskScoring { Id = 9702, CalculatedRisk = 7f, ResidualRisk = 6f, ClassicImpact = 4, ScoringMethod = 1 });
            await db.SaveChangesAsync();
        }

        await using (var conn = await OpenAsync())
            await ExecAsync(conn, string.Format(CultureInfo.InvariantCulture,
                "INSERT INTO risk_index_profiles (id, version, name, mode, status, parameters, justification, submitted_at, " +
                "approved_at, committee_decision_ref, effective_from, created_at) VALUES (1, 1, 'Pilot', 1, 3, '{0}', 'pilot', " +
                "'2026-08-25', '2026-08-31', 'CRTI-2026-09', '2026-09-01 05:00:00', '2026-08-20');",
                RiskIndexParametersJson.Serialize(new RiskIndexParameters()).Replace("'", "''")));

        var dal = new DalService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = fixture.ConnectionString + "ConvertZeroDateTime=True;"
        }).Build(), BackgroundServiceHttpContextAccessor.WithUnrestrictedEntityScope());
        var logger = Substitute.For<Serilog.ILogger>();
        foreach (var day in new[] { now.AddDays(-1), now })
            Assert.Equal(RiskIndexSnapshotResult.Written,
                (await new RiskIndexSnapshotService(dal, new RiskIndexDayCalculator(dal), logger).RunAsync(day)).Result);

        var reads = new RiskIndexReadService(dal, new RiskIndexReadCache(), new RiskIndexDerivedBudget(), logger,
            new PermissionsService(dal, new RolesService(logger, dal)));
        var admin = new RiskIndexCaller(1, true, new HashSet<string>());
        var unit = RiskIndexNodeKey.ForEntity(Unit).ToString();
        var date = DateOnly.FromDateTime(now);

        Assert.Equal(date, (await reads.GetLatestAsync())!.Date);
        Assert.Equal(2, (await reads.GetSnapshotDatesAsync(null, null)).Count);
        Assert.NotNull((await reads.GetOverviewAsync("org", null, admin)).Headline);
        Assert.Equal(RiskIndexProvenance.Official, (await reads.GetOverviewAsync(unit, null, admin)).Provenance);
        Assert.Equal(RiskIndexProvenance.OfficialDerived, (await reads.GetOverviewAsync(unit + ";c:devices", null, admin)).Provenance);
        Assert.Equal(2, (await reads.GetTrendAsync(unit, 30, RiskIndexCategory.Icr, null, admin)).Points.Count);
        Assert.Contains((await reads.GetNodesAsync("org", RiskIndexDimension.Unit, null, null, admin)).Rows, r => r.Node.Key == unit);
        var objects = await reads.GetObjectsAsync(unit, null, new RiskIndexObjectQuery(null, "-r,lastReading"), admin);
        Assert.True(objects.Total > 0);
        Assert.Equal("srv-01", (await reads.GetObjectAsync(RiskIndexObjectKind.Host, "9701", unit, null, admin)).Object.Label);
        Assert.True((await reads.GetInventoryAsync(unit, RiskIndexTabGroup.Devices, null, admin)).InventoryCount > 0);
        Assert.Equal(1.0, (await reads.GetGovernanceIndicatorsAsync(unit, null, admin)).SlaCompliance);
        Assert.NotNull(await reads.GetContributionsAsync(unit, null, null, admin));
        Assert.Equal(date.AddDays(-1), (await reads.GetChangesAsync(unit, null, null, admin)).From);

        // T253: the tab reads translate on MariaDB — class days and category rows by node, the day-by-day recomposition of
        // a derived node, and the factor and signal JSON behind the risk factors and the object list's factor filter.
        var classSummary = await reads.GetMonthlySummaryAsync(unit, RiskIndexTabGroup.Devices, 6, null, admin);
        Assert.Equal(RiskIndexProvenance.OfficialDerived, classSummary.Provenance);
        Assert.NotNull(classSummary.Months[^1].MeanByCategory[RiskIndexCategory.Exp]);
        var nodeSummary = await reads.GetMonthlySummaryAsync(unit, null, 12, null, admin);
        Assert.Equal((RiskIndexProvenance.Official, 12), (nodeSummary.Provenance, nodeSummary.Months.Count));
        Assert.NotNull(nodeSummary.Months[^1].MeanByCategory[RiskIndexCategory.Exp]);
        var derivedSummary = await reads.GetMonthlySummaryAsync(unit + ";c:devices", null, 6, null, admin);
        Assert.Equal(RiskIndexProvenance.OfficialDerived, derivedSummary.Provenance);
        Assert.NotNull(derivedSummary.Months[^1].MeanByCategory[RiskIndexCategory.Exp]);
        var factors = await reads.GetRiskFactorsAsync(unit, RiskIndexTabGroup.Devices, null, admin);
        Assert.NotEmpty(factors);
        var listed = await reads.GetObjectsAsync(unit + ";c:devices", null, new RiskIndexObjectQuery(factors[0].ObjectFilter), admin);
        Assert.Equal(factors[0].Count, listed.Rows.Select(r => r.Object.Object).Distinct().Count());

        // T256: the data quality translates on MariaDB — the node's object rows and their factor JSON for stale sources, the
        // organization's stored tree counts, and the chip filters the quality items open.
        var orgQuality = await reads.GetQualityAsync("org", null, admin);
        Assert.NotNull(orgQuality.OrphanApplications);
        Assert.InRange(orgQuality.DefaultCriticalityWeightFraction, 0, 1);
        var unitQuality = await reads.GetQualityAsync(unit, null, admin);
        Assert.Null(unitQuality.OrphanApplications);
        var presumed = await reads.GetObjectsAsync(unit, null,
            new RiskIndexObjectQuery(RiskIndexQualityFilters.Filter(RiskIndexQualityFilters.PresumedCritical)), admin);
        Assert.Equal(unitQuality.PresumedCritical, presumed.Rows.Select(r => r.Object.Object).Distinct().Count());

        Assert.Equal(RiskIndexPublicationState.Shadow, (await reads.GetPublicationAsync()).State);
    }

    /// <summary>
    /// T250 on MariaDB: the lifecycle end to end through the production data access — draft, the preview claimed by the
    /// set-based update and computed, submission, the segregated approval reading the approver's grants from the seeded
    /// roles, and the revision written as the concurrency token at each transition.
    /// </summary>
    [Fact]
    public async Task TestQ6_TheLifecycleRunsOnMariaDbWithTheSeededApproverRole()
    {
        await fixture.InitializeNumberedSchemaAsync(MariaDbContainerFixture.TargetSchemaVersion);
        const int author = 9801, approver = 9802;
        await using (var db = fixture.NewContext())
        {
            var analyst = await db.Roles.SingleAsync(r => r.Name == RiskIndexRoles.RiskAnalyst);
            var manager = await db.Roles.SingleAsync(r => r.Name == RiskIndexRoles.RiskManager);
            foreach (var (id, role) in new[] { (author, analyst.Value), (approver, manager.Value) })
                db.Users.Add(new User
                {
                    Value = id, Name = $"user {id}", Login = $"u{id}", Enabled = true, Type = "local", Salt = "s",
                    Password = System.Text.Encoding.UTF8.GetBytes("p"), Email = $"u{id}@example.invalid", RoleId = role
                });
            await db.SaveChangesAsync();
        }

        // Each actor's own principal (global scope, its user id), as the API builds it: the audit rows name them.
        DalService Dal(int user) => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = fixture.ConnectionString + "ConvertZeroDateTime=True;"
        }).Build(), new Microsoft.AspNetCore.Http.HttpContextAccessor
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                [
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Sid, user.ToString(CultureInfo.InvariantCulture)),
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, $"u{user}"),
                    new System.Security.Claims.Claim(BackgroundServiceHttpContextAccessor.ScopeClaimType, BackgroundServiceHttpContextAccessor.GlobalScope)
                ], "test"))
            }
        });
        var logger = Substitute.For<Serilog.ILogger>();
        RiskIndexProfilesService Profiles(int user)
        {
            var dal = Dal(user);
            return new RiskIndexProfilesService(dal, new PermissionsService(dal, new RolesService(logger, dal)),
                new AuditTrailService(logger, dal), new RiskIndexReadCache(), logger);
        }

        var profiles = Profiles(author);
        var previews = new RiskIndexPreviewService(Dal(author), logger);
        var asAuthor = new RiskIndexCaller(author, false, new HashSet<string> { RiskIndexPermissions.View, RiskIndexPermissions.Configure });
        var asApprover = new RiskIndexCaller(approver, false,
            new HashSet<string> { RiskIndexPermissions.View, RiskIndexPermissions.Configure, RiskIndexPermissions.Approve });

        var draft = await profiles.CreateAsync(new RiskIndexProfileCreateRequest("Pilot", null, RiskIndexPresets.Balanced), asAuthor);
        var edited = await profiles.UpdateAsync(draft.Id, new RiskIndexProfileUpdateRequest("Pilot", RiskIndexMode.Interim,
            draft.Parameters with { P = 6 }), draft.Revision, asAuthor);
        var stale = await Assert.ThrowsAsync<RiskIndexException>(() => profiles.UpdateAsync(draft.Id,
            new RiskIndexProfileUpdateRequest("Pilot", RiskIndexMode.Interim, draft.Parameters), draft.Revision, asAuthor));
        Assert.Equal(RiskIndexErrors.StaleDraft, stale.Code);

        await profiles.RequestPreviewAsync(draft.Id, asAuthor);
        Assert.Equal(RiskIndexPreviewRunResult.Completed, await previews.ProcessNextAsync(DateTime.UtcNow));
        await profiles.SubmitAsync(draft.Id, new RiskIndexSubmitRequest("Pilot profile for the shadow period."), asAuthor);

        var own = await Assert.ThrowsAsync<RiskIndexException>(() =>
            profiles.ApproveAsync(draft.Id, new RiskIndexApproveRequest("CRTI", "Board", false, null),
                asAuthor with { Permissions = asApprover.Permissions }));
        Assert.Equal(RiskIndexErrors.SodAuthorCannotApprove, own.Code);

        // The committee's minutes, uploaded by the approver and linked on MariaDB (S40 §6.4): without them, no approval.
        var noMinutes = await Assert.ThrowsAsync<RiskIndexException>(() => Profiles(approver).ApproveAsync(draft.Id,
            new RiskIndexApproveRequest("CRTI", "Board", false, null), asApprover));
        Assert.Equal(RiskIndexErrors.CommitteeAttachmentRequired, noMinutes.Code);
        int minutes;
        await using (var db = fixture.NewContext())
        {
            var file = new NrFile
            {
                Name = "ata-crti.pdf", UniqueName = "ata-crti-" + Guid.NewGuid().ToString("N"), Type = "1", Size = 3,
                Timestamp = DateTime.UtcNow, User = approver, Content = [1, 2, 3]
            };
            db.NrFiles.Add(file);
            await db.SaveChangesAsync();
            minutes = file.Id;
        }

        await Profiles(approver).AttachAsync(draft.Id, new RiskIndexAttachRequest(minutes), asApprover);
        Assert.Single(await profiles.ListAttachmentsAsync(draft.Id));

        var approved = await Profiles(approver).ApproveAsync(draft.Id, new RiskIndexApproveRequest("CRTI", "Board", false, null),
            asApprover);

        Assert.Equal(RiskIndexProfileStatus.Active, approved.Status);
        Assert.StartsWith("role:", approved.ApprovalGrantSource);
        Assert.Equal(edited.Revision + 2, approved.Revision);

        // Who wrote the draft is the profile's own record, not the audit trail's: the author is its creator and editor and
        // its submitter, written in the same saves as the edits.
        await using (var db = fixture.NewContext())
        {
            Assert.Equal(new int?[] { author }, await db.RiskIndexProfileEditors.Where(e => e.ProfileId == draft.Id)
                .Select(e => e.UserId).ToArrayAsync());
            Assert.Equal(author, (await db.RiskIndexProfiles.SingleAsync(p => p.Id == draft.Id)).SubmittedById);
        }
    }
}
