using System;
using System.Linq;
using DAL.Context;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;
using DalRiskStatus = DAL.Enums.RiskStatus;
using TransactionResult = DAL.Enums.TransactionResult;
using ModelRiskStatus = Model.Risks.RiskStatus;
using RiskHelper = Model.Risks.RiskHelper;

namespace ServerServices.Tests.Track6;

/// <summary>
/// Model-metadata + mapping unit tests for Track 6 Milestone 6.4 (no DB): the Phase 5 enum conversions are
/// configured and round-trip, the Phase 5 status value→enum mapping covers the unknown fallback, and the
/// Phase 6a deprecations (23 dead entities + the risks.regulation/project_id orphan columns) left the model.
/// </summary>
public class Track6Phase5And6ModelTests
{
    // The model is built with the real (relational) MySQL provider so value converters configured via
    // HasConversion are present — model building does NOT open a connection, so no DB is required. A fixed
    // ServerVersion avoids AutoDetect (which would connect).
    private static NRDbContext NewModelContext()
    {
        var options = new DbContextOptionsBuilder<NRDbContext>()
            .UseMySql("Server=localhost;Database=none;Uid=none;Pwd=none",
                new MySqlServerVersion(new Version(10, 11, 0)))
            .Options;
        return new NRDbContext(options);
    }

    [Fact]
    public void Phase5_RiskStatusId_HasValueConverter_RoundTrips()
    {
        using var ctx = NewModelContext();
        var prop = ctx.Model.FindEntityType(typeof(Risk))!.FindProperty(nameof(Risk.StatusId));
        Assert.NotNull(prop);
        // The enum persists as an int column via the configured conversion.
        Assert.Equal("int", prop!.GetColumnType());
        Assert.Equal(typeof(int), prop.GetProviderClrType());
        var converter = prop.FindRelationalTypeMapping()!.Converter;
        Assert.NotNull(converter);

        foreach (var status in new[] { DalRiskStatus.New, DalRiskStatus.MitigationPlanned, DalRiskStatus.ManagementReview, DalRiskStatus.Closed })
        {
            var stored = converter!.ConvertToProvider(status);
            Assert.Equal((int)status, stored);
            Assert.Equal(status, converter.ConvertFromProvider(stored));
        }
    }

    [Fact]
    public void Phase5_TransactionResult_HasExplicitIntConverter_RoundTrips()
    {
        using var ctx = NewModelContext();
        var prop = ctx.Model.FindEntityType(typeof(BiometricTransaction))!
            .FindProperty(nameof(BiometricTransaction.TransactionResult));
        Assert.NotNull(prop);
        Assert.Equal("int", prop!.GetColumnType());
        Assert.Equal(typeof(int), prop.GetProviderClrType());
        var converter = prop.FindRelationalTypeMapping()!.Converter;
        Assert.NotNull(converter);

        foreach (var result in new[] { TransactionResult.Success, TransactionResult.RequestError, TransactionResult.SuccessfullyCompleted })
        {
            var stored = converter!.ConvertToProvider(result);
            Assert.Equal((int)result, stored);
            Assert.Equal(result, converter.ConvertFromProvider(stored));
        }
    }

    [Theory]
    [InlineData("New", ModelRiskStatus.New)]
    [InlineData("Mitigation Planned", ModelRiskStatus.MitigationPlanned)]
    [InlineData("Mgmt Reviewed", ModelRiskStatus.ManagementReview)]
    [InlineData("Closed", ModelRiskStatus.Closed)]
    public void Phase5_StatusStringMapping_MapsKnownValues(string name, ModelRiskStatus expected)
    {
        Assert.Equal(expected, RiskHelper.GetRiskStatusFromName(name));
        // Round-trips with the forward mapping (documents parity with the SQL backfill).
        Assert.Equal(name, RiskHelper.GetRiskStatusName(expected));
    }

    [Theory]
    [InlineData("Some Legacy Value")]
    [InlineData("")]
    [InlineData(null)]
    public void Phase5_StatusStringMapping_UnknownIsNull(string? name)
    {
        Assert.Null(RiskHelper.GetRiskStatusFromName(name));
    }

    [Fact]
    public void Phase6a_DeprecatedEntities_AreUnmappedFromModel()
    {
        using var ctx = NewModelContext();
        var mapped = ctx.Model.GetEntityTypes().Select(e => e.ClrType.Name).ToHashSet();

        foreach (var dead in new[]
                 {
                     "ContributingRisksImpact", "ContributingRisksLikelihood", "QuestionnairePendingRisk",
                     "ResidualRiskScoringHistory", "FrameworkControlTestResultsToRisk", "FrameworkControlTypeMapping",
                     "PermissionToPermissionGroup", "MitigationAcceptUser", "RiskToAdditionalStakeholder",
                     "RiskToLocation", "RiskToTechnology", "FrameworkControlTestComment", "FrameworkControlTestAudit",
                     "FailedLoginAttempt", "UserPassHistory", "ControlPhase", "ControlType", "FileTypeExtension",
                     "Regulation", "RiskFunction", "TestStatus", "ThreatCatalog", "ThreatGrouping",
                 })
            Assert.DoesNotContain(dead, mapped);

        // UserPassReuseHistory is the live one — it must remain.
        Assert.Contains("UserPassReuseHistory", mapped);
    }

    [Fact]
    public void Phase6a_RiskOrphanColumns_AreUnmapped()
    {
        using var ctx = NewModelContext();
        var risk = ctx.Model.FindEntityType(typeof(Risk))!;
        Assert.Null(risk.FindProperty("Regulation"));
        Assert.Null(risk.FindProperty("ProjectId"));
        // The Phase 5 replacement coexists with the legacy text column.
        Assert.NotNull(risk.FindProperty(nameof(Risk.Status)));
        Assert.NotNull(risk.FindProperty(nameof(Risk.StatusId)));
    }
}
