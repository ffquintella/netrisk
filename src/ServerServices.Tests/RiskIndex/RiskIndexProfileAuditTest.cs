using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Auditing;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.RiskIndex;
using ServerServices.Governance;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// T243 (S39 §12.5, §13.1; S40 §7.5): a profile is audited field by field — not its attached preview nor its concurrency
/// token —, the snapshot and object rows are never audited, the two index settings are, and a profile's trail is served
/// to unrestricted callers only.
/// </summary>
[TestSubject(typeof(GovernanceAuditInterceptor))]
public class RiskIndexProfileAuditTest : InMemoryServiceTestBase
{
    private const int ProfileId = 7;

    private static readonly DateTime Created = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private static RiskIndexProfile Draft() => new()
    {
        Id = ProfileId, Version = 1, Name = "Pilot", Mode = RiskIndexMode.Interim, Status = RiskIndexProfileStatus.Draft,
        Parameters = RiskIndexParametersJson.Serialize(new RiskIndexParameters()), CreatedAt = Created
    };

    private AuditLog[] ProfileRows()
    {
        using var db = OpenContext();
        return db.AuditLogs.Where(a => a.EntityType == nameof(RiskIndexProfile)).OrderBy(a => a.Id).ToArray();
    }

    [Fact]
    public void TestTheProfileIsAuditedAndTheHistoryTablesAreNot()
    {
        Assert.Contains(nameof(RiskIndexProfile), GovernanceAuditInterceptor.AuditedTypes);
        Assert.DoesNotContain(nameof(RiskIndexSnapshot), GovernanceAuditInterceptor.AuditedTypes);
        Assert.DoesNotContain(nameof(RiskIndexObjectDay), GovernanceAuditInterceptor.AuditedTypes);
        Assert.Contains(RiskIndexSettingKeys.ApproverRoles, GovernanceAuditInterceptor.AuditedSettingNames);
        Assert.Contains(RiskIndexSettingKeys.Publication, GovernanceAuditInterceptor.AuditedSettingNames);
    }

    [Fact]
    public void TestCreatingAProfileWritesOneCreateRowWithoutThePreview()
    {
        Seed(ctx =>
        {
            var draft = Draft();
            draft.SensitivityReport = "{\"secret-preview\": true}";
            ctx.RiskIndexProfiles.Add(draft);
        });

        var row = Assert.Single(ProfileRows());
        Assert.Equal((AuditLogAction.Create, ProfileId), (row.Action, row.EntityId));
        Assert.Contains("Parameters=", row.NewValue);
        Assert.DoesNotContain("secret-preview", row.NewValue);
        Assert.DoesNotContain("Revision=", row.NewValue);
    }

    /// <summary>A transition and its evidence are field rows; the preview and the concurrency token changing with them are not.</summary>
    [Fact]
    public void TestATransitionIsRecordedFieldByFieldExceptThePreviewAndTheRevision()
    {
        Seed(ctx => ctx.RiskIndexProfiles.Add(Draft()));

        Seed(ctx =>
        {
            var profile = ctx.RiskIndexProfiles.Single(p => p.Id == ProfileId);
            profile.Status = RiskIndexProfileStatus.PendingApproval;
            profile.Justification = "Pilot profile for the shadow period.";
            profile.SubmittedAt = Created.AddDays(1);
            profile.SensitivityReport = "{\"nodes\": []}";
            profile.Revision = 3;
        });

        var fields = ProfileRows().Where(r => r.Action == AuditLogAction.Update).Select(r => r.Field).ToHashSet();

        Assert.Equal(new[] { nameof(RiskIndexProfile.Justification), nameof(RiskIndexProfile.Status), nameof(RiskIndexProfile.SubmittedAt) },
            fields.Order());
    }

    [Fact]
    public void TestChangingTheApproverRolesSettingIsAudited()
    {
        Seed(ctx => ctx.Settings.Add(new Setting { Name = RiskIndexSettingKeys.ApproverRoles, Value = RiskIndexRoles.DefaultApproverRoles }));
        Seed(ctx => ctx.Settings.Single(s => s.Name == RiskIndexSettingKeys.ApproverRoles).Value = "RiskManager");

        using var db = OpenContext();
        var update = Assert.Single(db.AuditLogs.Where(a => a.Field == RiskIndexSettingKeys.ApproverRoles && a.Action == AuditLogAction.Update));
        Assert.Equal((RiskIndexRoles.DefaultApproverRoles, "RiskManager"), (update.OldValue, update.NewValue));
    }

    /// <summary>The weights' history is read with global scope (S40 §8.4): a scoped reader is refused it, wherever they sit.</summary>
    [Fact]
    public async Task TestAScopedReaderIsRefusedTheProfileTrail()
    {
        SeedUnscoped(ctx => ctx.RiskIndexProfiles.Add(Draft()));
        var trail = GetService<IAuditTrailService>();

        ScopeTo(100);

        await Assert.ThrowsAsync<DataNotFoundException>(() => trail.GetForRecordAsync(nameof(RiskIndexProfile), ProfileId));
    }

    [Fact]
    public async Task TestAnUnrestrictedReaderReadsTheProfileTrail()
    {
        SeedUnscoped(ctx => ctx.RiskIndexProfiles.Add(Draft()));
        var trail = GetService<IAuditTrailService>();

        ScopeToEverything();

        var rows = await trail.GetForRecordAsync(nameof(RiskIndexProfile), ProfileId);
        Assert.Contains(rows, r => r.Action == AuditLogAction.Create);
    }
}
