using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using GUIClient.Tests.Resources;
using GUIClient.Tools;
using JetBrains.Annotations;
using Model.Authentication;
using Model.Continuity;
using Xunit;

namespace GUIClient.Tests.Tools;

/// <summary>
/// Stage 9.3 (S43 §8, U1–U5) — the pure half of the continuity screens: how a duration reads and
/// converts, that every computed key exists in the three resource files, who sees and edits what, and
/// the parameter form's bounds.
/// </summary>
[TestSubject(typeof(ContinuitySummary))]
public class ContinuitySummaryTest
{
    /// <summary>U1 — null reads "Absent" (never 0, never blank); minutes, hours and days compose.</summary>
    [Theory]
    [InlineData(null, "Absent")]
    [InlineData(0, "0 min")]
    [InlineData(90, "1 h 30 min")]
    [InlineData(1_440, "1 d")]
    [InlineData(2_880, "2 d")]
    [InlineData(1_501, "1 d 1 h 1 min")]
    public void TestU1_ADurationReads(int? minutes, string expected)
    {
        Assert.Equal(expected, ContinuitySummary.Duration(minutes, "Absent"));
    }

    /// <summary>U2 — a typed value converts to minutes; empty is "not declared"; negative, fractional
    /// minutes and anything past 365 days are invalid, with no overflow on a huge value.</summary>
    [Theory]
    [InlineData(null, DurationUnit.Hours, true, null)]
    [InlineData(4.0, DurationUnit.Hours, true, 240)]
    [InlineData(365.0, DurationUnit.Days, true, 525_600)]
    [InlineData(0.5, DurationUnit.Hours, true, 30)]
    [InlineData(366.0, DurationUnit.Days, false, null)]
    [InlineData(-1.0, DurationUnit.Minutes, false, null)]
    [InlineData(0.5, DurationUnit.Minutes, false, null)]
    [InlineData(1e15, DurationUnit.Days, false, null)]
    public void TestU2_ATypedValueConvertsToMinutes(double? value, DurationUnit unit, bool valid, int? expected)
    {
        Assert.Equal(valid, ContinuitySummary.TryToMinutes(value is null ? null : (decimal)value.Value, unit, out var minutes));
        Assert.Equal(expected, minutes);
    }

    [Theory]
    [InlineData(2_880, 2, DurationUnit.Days)]
    [InlineData(240, 4, DurationUnit.Hours)]
    [InlineData(90, 90, DurationUnit.Minutes)]
    [InlineData(0, 0, DurationUnit.Minutes)]
    public void TestU2_AStoredValueReopensInItsLargestExactUnit(int minutes, int value, DurationUnit unit)
    {
        Assert.Equal(((decimal)value, unit), ContinuitySummary.ForEditing(minutes));
    }

    /// <summary>U3 — each status, reason, source, threat reason, outcome and unit has its key, and every
    /// computed key resolves in the three resource files (<c>LocalizationCoverageTest</c> sees only literals).</summary>
    [Theory]
    [InlineData("Localization.resx")]
    [InlineData("Localization.en-US.resx")]
    [InlineData("Localization.pt-BR.resx")]
    public void TestU3_EveryComputedKeyResolvesIn(string file)
    {
        var declared = XDocument.Load(EntityConfigurationSchema.ResourcePath(file)).Root!
            .Elements("data")
            .Select(d => d.Attribute("name")?.Value)
            .ToHashSet(StringComparer.Ordinal);

        var missing = ContinuitySummary.ComputedKeys.Where(k => !declared.Contains(k)).ToList();

        Assert.True(missing.Count == 0, $"{file} does not declare: {string.Join(", ", missing)}");
        Assert.Equal(3 + 4 + 5 + 3 + 8 + 2, ContinuitySummary.ComputedKeys.Count);
    }

    [Fact]
    public void TestU3_TheKeysAreDistinctPerValue()
    {
        Assert.Equal("VerificationUnverified", ContinuitySummary.StatusKey(ObjectiveVerificationStatus.Unverified));
        Assert.Equal("VerificationAbsent", ContinuitySummary.StatusKey(ObjectiveVerificationStatus.Absent));
        Assert.Equal("VerificationReasonStale", ContinuitySummary.ReasonKey(VerificationReason.Stale));
        Assert.Equal("CriticalityNotDeclared", ContinuitySummary.SourceKey(null));
        Assert.Equal("CriticalitySourceBia", ContinuitySummary.SourceKey(CriticalitySource.Bia));
        Assert.Equal("ThreatReasonProviderUnverified", ContinuitySummary.ThreatReasonKey(ContinuityThreatReason.ProviderUnverified));
        Assert.Equal("TestOutcomeFailed", ContinuitySummary.OutcomeKey(DAL.Enums.RestorationTestOutcome.Failed));
    }

    private static AuthenticatedUserInfo User(bool admin = false, string? role = null, params string[] permissions) => new()
    {
        IsAdmin = admin, UserRole = role, UserPermissions = permissions.ToList()
    };

    /// <summary>U4 — the read audience mirrors RequireContinuityRead; the parameters, RequireAdminOnly.</summary>
    [Fact]
    public void TestU4_WhoSeesAndWhoEdits()
    {
        Assert.True(ContinuityAccess.CanRead(User(admin: true)));
        Assert.True(ContinuityAccess.CanRead(User(role: "Administrator")));
        Assert.True(ContinuityAccess.CanRead(User(permissions: "riskmanagement")));
        Assert.True(ContinuityAccess.CanRead(User(permissions: "bia_manage")));
        Assert.True(ContinuityAccess.CanRead(User(permissions: "restoration_test_record")));
        Assert.False(ContinuityAccess.CanRead(User(permissions: "submit_risks")));
        Assert.False(ContinuityAccess.CanRead(null));

        Assert.True(ContinuityAccess.CanEditSettings(User(admin: true)));
        Assert.True(ContinuityAccess.CanEditSettings(User(role: "Administrator")));
        Assert.False(ContinuityAccess.CanEditSettings(User(permissions: "bia_manage")));
        Assert.False(ContinuityAccess.CanEditSettings(null));
    }

    /// <summary>U5 — the parameter form at its bounds, the key of each message, and how the weight and the
    /// threat headline read.</summary>
    [Theory]
    [InlineData(0, 0.5, "RestorationTestValidityDaysHint")]
    [InlineData(1096, 0.5, "RestorationTestValidityDaysHint")]
    [InlineData(null, 0.5, "RestorationTestValidityDaysHint")]
    [InlineData(1, 0.0, "UnverifiedThreatWeightHint")]
    [InlineData(1095, 1.01, "UnverifiedThreatWeightHint")]
    [InlineData(1, 0.01, null)]
    [InlineData(1095, 1.0, null)]
    public void TestU5_TheParameterFormBounds(int? days, double weight, string? expected)
    {
        Assert.Equal(expected, ContinuitySummary.SettingsErrorKey(days, (decimal)weight));
    }

    [Fact]
    public void TestU5_TheWeightAndTheThreatHeadlineRead()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            Assert.Equal("0.50", ContinuitySummary.Weight(0.5m));
            Assert.Equal("None", ContinuitySummary.ThreatHeadline(null, "None", "C {0}", "U {0}"));
            Assert.Equal("None", ContinuitySummary.ThreatHeadline(new ContinuityThreatDto(), "None", "C {0}", "U {0}"));

            var unverified = new ContinuityThreatDto
            {
                ThreatWeight = 0.5m,
                Items = [new ContinuityThreatItemDto { Class = ContinuityThreatClass.NotVerified, Weight = 0.5m }]
            };
            Assert.Equal("U 0.50", ContinuitySummary.ThreatHeadline(unverified, "None", "C {0}", "U {0}"));

            unverified.Items.Add(new ContinuityThreatItemDto { Class = ContinuityThreatClass.Confirmed, Weight = 1m });
            unverified.ThreatWeight = 1m;
            Assert.Equal("C 1.00", ContinuitySummary.ThreatHeadline(unverified, "None", "C {0}", "U {0}"));

            Thread.CurrentThread.CurrentCulture = new CultureInfo("pt-BR");
            Assert.Equal("0,50", ContinuitySummary.Weight(0.5m));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void TestOnlyProcessesAndServicesAreSubjects()
    {
        Assert.True(ContinuitySummary.IsSubject("businessProcess"));
        Assert.True(ContinuitySummary.IsSubject("itService"));
        Assert.False(ContinuitySummary.IsSubject("application"));
        Assert.False(ContinuitySummary.IsSubject(null));
    }
}
