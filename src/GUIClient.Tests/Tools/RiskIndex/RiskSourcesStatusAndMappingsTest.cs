using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DAL.Entities;
using DAL.Enums;
using GUIClient.Tools.RiskIndex;
using JetBrains.Annotations;
using Model.Authentication;
using Model.Integrations;
using Model.RiskIndex;
using Xunit;

namespace GUIClient.Tests.Tools.RiskIndex;

/// <summary>
/// M55, T263 (S40 §6.5, §6.6, §12.4): how the Sources tab words the server's operational columns — withheld counts read "—"
/// with the reason, never 0 — and the gates of the entity-mapping panel, each closed with the key its tooltip shows; plus
/// every key those screens and the new refusals name, in the three resource files.
/// </summary>
[TestSubject(typeof(EntityMappingGates))]
public class RiskSourcesStatusAndMappingsTest
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

    private static VendorEntityMappingDto Rule(int id, string value, int priority = 1,
        VendorMatchKind kind = VendorMatchKind.V1CustomTag) =>
        new(id, 2, 1, kind, value, null, 8, "Law School", priority, RestampPolicy.OnlyUnassigned, true);

    // --- the operational columns -----------------------------------------------------------------------------------------

    [Fact]
    public void TestASyncedSourceReadsItsConnectionsLastSyncStatusAndCounts()
    {
        var source = new RiskIndexSourceDto(RiskIndexSources.V1Device, 2, new DateTime(2026, 10, 5, 3, 12, 0, DateTimeKind.Utc),
            RiskIndexSourceStatusKeys.Ok, RiskIndexSourceStatusKeys.KindSync, 16831, 0.18, false);

        var cells = RiskSourceStatusFormatter.Cells(source, RiskIndexTestText.Portuguese, Culture);

        Assert.Equal(("2", "ok", "16.831", "18%", ""), (cells.Connections, cells.Status, cells.Objects,
            cells.Stale.Replace(" ", ""), cells.CountsHint));
        Assert.NotEqual(RiskSourceStatusFormatter.Dash, cells.LastSync);
    }

    [Fact]
    public void TestWithheldCountsReadAsADashWithTheReasonNeverZero()
    {
        var source = new RiskIndexSourceDto(RiskIndexSources.V1Cve, 1, null, null, RiskIndexSourceStatusKeys.KindSync, null, null,
            true);

        var cells = RiskSourceStatusFormatter.Cells(source, RiskIndexTestText.Portuguese, Culture);

        Assert.Equal((RiskSourceStatusFormatter.Dash, RiskSourceStatusFormatter.Dash, RiskSourceStatusFormatter.Dash),
            (cells.Objects, cells.Stale, cells.LastSync));
        Assert.Equal(RiskIndexTestText.Portuguese(RiskSourceStatusFormatter.RequiresGlobalScope), cells.CountsHint);
    }

    [Fact]
    public void TestAnImportReadsAsAnImportAndAnUnknownSourceAsDashes()
    {
        var import = new RiskIndexSourceDto(RiskIndexSources.TenableNessus, null, new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc),
            RiskIndexSourceStatusKeys.Failed, RiskIndexSourceStatusKeys.KindImport, null, null, false);

        var cells = RiskSourceStatusFormatter.Cells(import, RiskIndexTestText.Portuguese, Culture);
        Assert.StartsWith("importação ", cells.LastSync);
        Assert.Equal(("—", "falhou"), (cells.Connections, cells.Status));

        Assert.All(new[] { RiskSourceStatusFormatter.Cells(null, RiskIndexTestText.Portuguese, Culture) }
            .SelectMany(c => new[] { c.Connections, c.LastSync, c.Status, c.Objects, c.Stale }),
            v => Assert.Equal(RiskSourceStatusFormatter.Dash, v));
    }

    [Fact]
    public void TestARunLineSaysWhatRanHowItEndedAndItsSummary()
    {
        var run = new IntegrationSyncLog
        {
            ConnectionName = "Risk index profile v3", StartedAt = new DateTime(2026, 10, 5, 5, 0, 0, DateTimeKind.Utc),
            FinishedAt = new DateTime(2026, 10, 5, 5, 7, 0, DateTimeKind.Utc), Status = IntegrationSyncStatus.PartiallySucceeded,
            Summary = "35210 object rows"
        };

        var line = RiskSourceStatusFormatter.RunLine(run, RiskIndexTestText.English, CultureInfo.InvariantCulture);

        Assert.StartsWith("Risk index profile v3 · ", line);
        Assert.Contains(" · partial · 35210 object rows", line);
    }

    // --- the entity-mapping panel ----------------------------------------------------------------------------------------

    [Fact]
    public void TestWithoutTheIntegrationPermissionEveryWriteIsClosedWithItsReason()
    {
        var draft = EntityMappingDraft.New(1) with { Value = "BU: Law", EntityId = 8 };

        Assert.Equal(EntityMappingGates.RequiresIntegrations, EntityMappingGates.New(false, 1).ReasonKey);
        Assert.Equal(EntityMappingGates.RequiresIntegrations, EntityMappingGates.Save(false, draft, true, []).ReasonKey);
        Assert.Equal(EntityMappingGates.RequiresIntegrations, EntityMappingGates.Delete(false, draft with { Id = 3 }).ReasonKey);
    }

    [Fact]
    public void TestNewNeedsAConnection()
    {
        Assert.Equal(EntityMappingGates.ChooseConnection, EntityMappingGates.New(true, null).ReasonKey);
        Assert.True(EntityMappingGates.New(true, 1).Enabled);
    }

    [Fact]
    public void TestSaveNeedsAChangedCompleteAndUniqueRule()
    {
        var rules = new[] { Rule(3, "BU: Law"), Rule(4, "Site: Rio", 2) };
        var complete = EntityMappingDraft.New(3) with { Value = "BU: Medicine", EntityId = 9 };

        Assert.Equal(EntityMappingGates.SelectRule, EntityMappingGates.Save(true, null, true, rules).ReasonKey);
        Assert.Equal(EntityMappingGates.NoChanges, EntityMappingGates.Save(true, complete, false, rules).ReasonKey);
        Assert.Equal(EntityMappingGates.Incomplete,
            EntityMappingGates.Save(true, complete with { EntityId = null }, true, rules).ReasonKey);
        Assert.Equal(EntityMappingGates.Incomplete, EntityMappingGates.Save(true, complete with { Value = " " }, true, rules).ReasonKey);
        Assert.Equal(EntityMappingGates.Duplicate,
            EntityMappingGates.Save(true, complete with { Value = "bu:law" }, true, rules).ReasonKey);
        Assert.True(EntityMappingGates.Save(true, complete, true, rules).Enabled);
        // A rule keeps its own value, and the same value under another kind is another rule.
        Assert.True(EntityMappingGates.Save(true, EntityMappingDraft.From(rules[0]) with { Priority = 9 }, true, rules).Enabled);
        Assert.True(EntityMappingGates.Save(true, complete with { Value = "BU: Law", Kind = VendorMatchKind.V1AssetGroup }, true,
            rules).Enabled);
    }

    [Fact]
    public void TestDeleteNeedsAStoredRuleAndThePreviewACompleteOne()
    {
        var draft = EntityMappingDraft.New(1) with { Value = "BU: Law", EntityId = 8 };

        Assert.Equal(EntityMappingGates.SelectRule, EntityMappingGates.Delete(true, draft).ReasonKey);
        Assert.True(EntityMappingGates.Delete(true, draft with { Id = 3 }).Enabled);
        Assert.True(EntityMappingGates.Preview(draft).Enabled);
        Assert.Equal(EntityMappingGates.Incomplete, EntityMappingGates.Preview(draft with { EntityId = null }).ReasonKey);
        Assert.Equal(EntityMappingGates.Incomplete, EntityMappingGates.Preview(null).ReasonKey);
    }

    [Fact]
    public void TestADraftIsCleanUntilItSaysSomethingElseAndSendsItsCanonicalValue()
    {
        var stored = Rule(3, "BU: Law");
        var draft = EntityMappingDraft.From(stored);

        Assert.True(draft.SameAs(stored));
        Assert.False((draft with { Value = "bu: law" }).SameAs(stored));
        Assert.False((draft with { Priority = 2 }).SameAs(stored));
        Assert.False(draft.SameAs(null));

        var body = (draft with { Value = "  BU:Law " }).ToUpsert(2, 1);
        Assert.Equal(("BU: Law", 8, 2, 1), (body.Value, body.EntityId, body.Provider, body.ConnectionId));
    }

    [Fact]
    public void TestANewRuleGoesLast()
    {
        Assert.Equal(1, EntityMappingGates.NextPriority([]));
        Assert.Equal(6, EntityMappingGates.NextPriority([Rule(1, "a", 5), Rule(2, "b", 2)]));
        Assert.Equal(EntityMappingGates.MaxPriority, EntityMappingGates.NextPriority([Rule(1, "a", EntityMappingGates.MaxPriority)]));
    }

    [Fact]
    public void TestTheIntegrationPermissionIsReadFromTheUser()
    {
        Assert.True(RiskIndexAdminRights.From(new AuthenticatedUserInfo
        {
            UserName = "u", UserPermissions = [RiskIndexPermissions.View, RiskIndexAdminRights.IntegrationsPermission]
        }).Integrations);
        Assert.False(RiskIndexAdminRights.From(new AuthenticatedUserInfo
        {
            UserName = "u", UserPermissions = [RiskIndexPermissions.View, RiskIndexPermissions.Configure]
        }).Integrations);
        Assert.True(RiskIndexAdminRights.From(new AuthenticatedUserInfo { UserName = "a", IsAdmin = true, UserPermissions = [] })
            .Integrations);
    }

    // --- the text --------------------------------------------------------------------------------------------------------

    /// <summary>Every key the two panels and the new refusals name, so none renders as its own name (S40 §10.5).</summary>
    [Fact]
    public void TestEveryKeyTheSourcesAndMappingScreensNameIsTranslated()
    {
        string[] statuses =
        [
            RiskIndexSourceStatusKeys.Ok, RiskIndexSourceStatusKeys.Partial, RiskIndexSourceStatusKeys.Failed,
            RiskIndexSourceStatusKeys.Running, RiskIndexSourceStatusKeys.Queued, RiskIndexSourceStatusKeys.Cancelled
        ];
        string[] outcomes =
        [
            VendorMappingOutcomes.Assign, VendorMappingOutcomes.Reassign, VendorMappingOutcomes.Conflict,
            VendorMappingOutcomes.AlreadyAssigned
        ];
        // The refusals of /Integrations/EntityMappings (VendorEntityMappingErrors) and of the Sources reads (RiskIndexErrors).
        string[] codes =
        [
            "mapping_requires_global_scope", "invalid_provider", "invalid_kind_for_provider", "connection_not_found",
            "value_required", "value_too_long", "mapping_entity_not_found", "invalid_priority", "invalid_policy",
            "duplicate_mapping", "mapping_not_found", "runs_require_global_scope", "run_kind_not_recorded", "invalid_run_kind"
        ];
        var keys = statuses.Select(s => "RiskSourceStatus_" + s)
            .Concat(outcomes.Select(o => "EntityMappingOutcome_" + o))
            .Concat(codes.Select(RiskApiErrorText.ResourceKey))
            .Concat(Enum.GetNames<RestampPolicy>().Select(p => "EntityMappingPolicy_" + p))
            .Concat(new[] { VendorMatchKind.V1CustomTag, VendorMatchKind.V1AssetGroup }.Select(k => "EntityMappingKind_" + k))
            .Concat(new[]
            {
                EntityMappingGates.RequiresIntegrations, EntityMappingGates.ChooseConnection, EntityMappingGates.Incomplete,
                EntityMappingGates.Duplicate, EntityMappingGates.SelectRule, EntityMappingGates.NoChanges,
                RiskSourceStatusFormatter.RequiresGlobalScope, "RiskSourcesImportFMT", "SyncCustomTags"
            })
            .Concat(KeysNamedBy("ViewModels/Admin/RiskIndex/EntityMappingsViewModel.cs"))
            .Concat(KeysNamedBy("ViewModels/Admin/RiskIndex/RiskSourcesViewModel.cs"));

        var files = RiskIndexTestText.ResourceFiles.Select(f => (f, RiskIndexTestText.Load(f))).ToList();
        var missing = keys.Distinct().SelectMany(k => files.Where(f => !f.Item2.TryGetValue(k, out var v) || string.IsNullOrWhiteSpace(v))
            .Select(f => $"{f.f}: {k}")).ToList();
        Assert.True(missing.Count == 0, "Missing resources: " + string.Join(", ", missing));
    }

    /// <summary>The literal keys a view-model reads through <c>Localizer["…"]</c> or <c>RiskAdminText.Text("…")</c>.</summary>
    private static IEnumerable<string> KeysNamedBy(string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(RiskIndexTestText.ClientDirectory(), relativePath));
        return System.Text.RegularExpressions.Regex
            .Matches(source, @"(?:Localizer\[|RiskAdminText\.Text\()""(?<key>[A-Za-z0-9_]+)""")
            .Select(m => m.Groups["key"].Value)
            // A prefix completed at run time ("EntityMappingOutcome_" + key) is covered by the lists above.
            .Where(k => !k.EndsWith('_'));
    }
}
