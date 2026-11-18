using System;
using System.Linq;
using System.Threading.Tasks;
using Contracts.Importers;
using DAL.Auditing;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using ServerServices.Governance;
using ServerServices.Interfaces;
using ServerServices.Services;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Security;

/// <summary>
/// T283 (S39 D-22, §9.5): the product inputs that move the risk index without a profile change — the review cadence basis,
/// the quantitative bands, the residual strategy and the SLA policy — leave an audit row when they change. None was on
/// the interceptor's allowlist before. <c>review_levels</c> and <c>risk_levels</c> are listed too, but they are keyless in
/// the schema and the model, so no EF write of either exists: pinned below. An out-of-band SQL edit stays outside any
/// save-time trail; the index's snapshot hash (S39 §9.5) is what sees it.
/// </summary>
[TestSubject(typeof(GovernanceAuditInterceptor))]
public class IndexInputsAuditTest : InMemoryServiceTestBase
{
    private const int UnitA = 100, UnitB = 200;

    public static TheoryData<string, string, string> IndexSettings() => new()
    {
        { GovernanceSettingKeys.NextReviewDateUses, "InherentRisk", "ResidualRisk" },
        { GovernanceSettingKeys.QuantitativeBandThresholds, "10000,100000,1000000", "5000,50000,500000" },
        { GovernanceSettingKeys.RiskWorkflowResidualStrategy, "MitigationPercent", "ControlEffectiveness" }
    };

    private AuditLog[] Rows(string entityType, string? field = null)
    {
        using var db = OpenContext();
        return db.AuditLogs.Where(a => a.EntityType == entityType && (field == null || a.Field == field))
            .OrderBy(a => a.Id).ToArray();
    }

    [Theory]
    [MemberData(nameof(IndexSettings))]
    public void TestSettingAndEditingAnIndexSettingIsAudited(string key, string seeded, string edited)
    {
        Seed(ctx => ctx.Settings.Add(new Setting { Name = key, Value = seeded }));
        Seed(ctx => ctx.Settings.Single(s => s.Name == key).Value = edited);

        var rows = Rows(nameof(Setting), key);

        Assert.Equal(2, rows.Length);
        Assert.Equal((AuditLogAction.Create, (string?)null, (string?)seeded),
            (rows[0].Action, rows[0].OldValue, rows[0].NewValue));
        Assert.Equal((AuditLogAction.Update, (string?)seeded, (string?)edited),
            (rows[1].Action, rows[1].OldValue, rows[1].NewValue));
    }

    [Theory]
    [MemberData(nameof(IndexSettings))]
    public void TestDeletingAnIndexSettingIsAudited(string key, string seeded, string _)
    {
        Seed(ctx => ctx.Settings.Add(new Setting { Name = key, Value = seeded }));
        Seed(ctx => ctx.Settings.Remove(ctx.Settings.Single(s => s.Name == key)));

        var delete = Assert.Single(Rows(nameof(Setting), key), r => r.Action == AuditLogAction.Delete);
        Assert.Equal(seeded, delete.OldValue);
    }

    /// <summary>The services read the keys the trail records: one constant, aliased, so the two cannot drift apart.</summary>
    [Fact]
    public void TestTheServicesReadTheAuditedKeys()
    {
        Assert.Equal(GovernanceSettingKeys.NextReviewDateUses, MgmtReviewsService.CadenceBasisSetting);
        Assert.Equal(GovernanceSettingKeys.QuantitativeBandThresholds, QuantitativeRiskService.BandThresholdsSetting);
        Assert.Equal(GovernanceSettingKeys.RiskWorkflowResidualStrategy, RiskCalculationService.ResidualStrategySetting);

        Assert.Contains(GovernanceSettingKeys.NextReviewDateUses, GovernanceAuditInterceptor.AuditedSettingNames);
        Assert.Contains(GovernanceSettingKeys.QuantitativeBandThresholds, GovernanceAuditInterceptor.AuditedSettingNames);
        Assert.Contains(GovernanceSettingKeys.RiskWorkflowResidualStrategy, GovernanceAuditInterceptor.AuditedSettingNames);
    }

    /// <summary>
    /// Changing the SLA policy through the product path inserts the new row and closes the old one: both are recorded —
    /// the insert as a create row with its allowances, the supersession as the old row's <c>EffectiveTo</c>.
    /// </summary>
    [Fact]
    public async Task TestChangingTheSlaPolicyIsAudited()
    {
        var sla = GetService<ISlaService>();

        var first = await sla.SetConfigurationAsync(new SlaConfiguration
            { Severity = (int)NormalizedSeverity.High, MaxTriageDays = 5, MaxRemediationDays = 30 }, userId: 1);
        await sla.SetConfigurationAsync(new SlaConfiguration
            { Severity = (int)NormalizedSeverity.High, MaxTriageDays = 5, MaxRemediationDays = 60 }, userId: 1);

        var rows = Rows(nameof(SlaConfiguration));

        var creates = rows.Where(r => r.Action == AuditLogAction.Create).ToArray();
        Assert.Equal(2, creates.Length);
        Assert.Contains("MaxRemediationDays=30", creates[0].NewValue);
        Assert.Contains("MaxRemediationDays=60", creates[1].NewValue);

        var superseded = Assert.Single(rows, r => r.Action == AuditLogAction.Update);
        Assert.Equal((first.Id, nameof(SlaConfiguration.EffectiveTo), (string?)null),
            (superseded.EntityId, superseded.Field, superseded.OldValue));
        Assert.NotNull(superseded.NewValue);
    }

    /// <summary>An entity override's trail is read through the row's own scope filter, like every other audited record.</summary>
    [Fact]
    public async Task TestAnSlaOverridesTrailIsReadInsideItsEntityOnly()
    {
        SeedUnscoped(ctx =>
        {
            ctx.SlaConfigurations.Add(new SlaConfiguration
            {
                Id = 1, Severity = (int)NormalizedSeverity.High, MaxTriageDays = 5, MaxRemediationDays = 30, EntityId = UnitA,
                EffectiveFrom = DateTime.UtcNow, CreatedAt = DateTime.UtcNow
            });
            ctx.SlaConfigurations.Add(new SlaConfiguration
            {
                Id = 2, Severity = (int)NormalizedSeverity.High, MaxTriageDays = 5, MaxRemediationDays = 15, EntityId = UnitB,
                EffectiveFrom = DateTime.UtcNow, CreatedAt = DateTime.UtcNow
            });
        });
        var trail = GetService<IAuditTrailService>();

        ScopeTo(UnitA);

        Assert.Single(await trail.GetForRecordAsync(nameof(SlaConfiguration), 1));
        await Assert.ThrowsAsync<DataNotFoundException>(() => trail.GetForRecordAsync(nameof(SlaConfiguration), 2));
    }

    /// <summary>
    /// The cadence tables are listed, so the day either gains a key in the model its writes are recorded with no further
    /// change. Today neither has one, and the context refuses to track either — so no EF path writes them unaudited.
    /// </summary>
    [Theory]
    [InlineData(typeof(ReviewLevel))]
    [InlineData(typeof(RiskLevel))]
    public void TestTheCadenceTablesAreListedAndHaveNoUnauditedWritePath(Type type)
    {
        Assert.Contains(type.Name, GovernanceAuditInterceptor.AuditedTypes);

        using var db = OpenContext();
        Assert.Null(db.Model.FindEntityType(type)!.FindPrimaryKey());
        Assert.Throws<InvalidOperationException>(() => db.Add(Activator.CreateInstance(type)!));
    }
}
