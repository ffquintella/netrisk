using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DAL.Enums;
using GUIClient.Tools.RiskIndex;
using Model.RiskIndex;
using Xunit;

namespace GUIClient.Tests.Tools.RiskIndex;

/// <summary>
/// M55 (S40 §10.5): the configuration screens read their text from the three resource files — every parameter of the
/// schema has its label and help (T251's raw keys are gone), every validation code and API refusal of the configuration
/// routes is translated, every key the new screens name exists — and an issue reads as a sentence with its numbers.
/// </summary>
public class RiskConfigurationTextTest
{
    private static readonly IReadOnlyDictionary<string, string>[] Files =
        RiskIndexTestText.ResourceFiles.Select(RiskIndexTestText.Load).ToArray();

    private static void AssertDeclaredEverywhere(IEnumerable<string> keys)
    {
        var missing = keys.SelectMany(k => RiskIndexTestText.ResourceFiles.Zip(Files)
                .Where(f => !f.Second.TryGetValue(k, out var v) || string.IsNullOrWhiteSpace(v))
                .Select(f => $"{f.First}: {k}"))
            .ToList();
        Assert.True(missing.Count == 0, "Missing resources: " + string.Join(", ", missing));
    }

    [Fact]
    public void TestEverySchemaParameterHasALabelAndAHelpInEveryLanguage()
    {
        var schema = RiskIndexProfileRules.Schema();
        AssertDeclaredEverywhere(schema.Items.SelectMany(i => new[] { ProfileParameterRows.LabelKey(i.Key), i.HelpKey }));
        AssertDeclaredEverywhere(schema.Items.Where(i => i.LockedReasonKey is not null).Select(i => i.LockedReasonKey!));
        AssertDeclaredEverywhere(ProfileParameterRows.GroupOrder.Select(ProfileParameterRows.GroupLabelKey));
        Assert.All(schema.Items, i => Assert.Equal(ProfileParameterRows.HelpKey(i.Key), i.HelpKey));
    }

    [Fact]
    public void TestEveryValidationCodeIsTranslated()
    {
        var keys = ProfileIssueText.ArgumentOrder.Keys.Where(c => c != "bands_invalid").Select(c => "RiskIssue_" + c)
            .Concat(new[] { "count", "first_not_zero", "not_increasing", "label_missing", "label_not_unique" }
                .Select(r => "RiskIssue_bands_invalid_" + r));
        AssertDeclaredEverywhere(keys);
    }

    [Fact]
    public void TestEveryCodeTheRulesEmitHasAnArgumentOrder()
    {
        var source = File.ReadAllText(Path.Combine(RiskIndexTestText.ClientDirectory(), "..", "Model", "RiskIndex",
            "RiskIndexProfileRules.cs"));
        var codes = Regex.Matches(source, @"Issue\(""(?<code>[a-z_]+)""").Select(m => m.Groups["code"].Value).Distinct();
        Assert.All(codes, c => Assert.True(ProfileIssueText.ArgumentOrder.ContainsKey(c), $"{c} has no argument order"));
    }

    [Fact]
    public void TestEveryEnumeratedLabelOfTheScreensExists()
    {
        AssertDeclaredEverywhere(RiskIndexPresets.All.Select(p => "RiskPreset_" + p));
        AssertDeclaredEverywhere(Enum.GetNames<RiskIndexRebaselineTrigger>().Select(t => "RiskRebaseline_" + t));
        AssertDeclaredEverywhere(Enum.GetNames<RiskIndexApprovalCheckCode>().Select(c => "RiskApprovalCheck_" + c));
        AssertDeclaredEverywhere(Enum.GetNames<RiskIndexPublicationState>().Select(s => "RiskPublication_" + s));
        AssertDeclaredEverywhere(Enum.GetNames<RiskSourceAvailability>().Select(a => "RiskSourceAvailability_" + a));
        AssertDeclaredEverywhere(new[] { "RiskPreviewState_NotRun", "RiskPreviewState_Queued", "RiskPreviewState_Stale" });
        AssertDeclaredEverywhere(Enumerable.Range(1, 5).Select(c => "RiskContextCriticality_" + c));
        AssertDeclaredEverywhere(Enum.GetNames<RiskIndexProfileStatus>().Select(RiskOverviewTopBarStatusKey));

        static string RiskOverviewTopBarStatusKey(string status) => "RiskProfileStatus_" + status;
    }

    [Fact]
    public void TestEveryRefusalOfTheConfigurationRoutesIsTranslated()
    {
        string[] codes =
        [
            "open_draft_exists", "preset_required_for_first_profile", "preset_not_activatable", "stale_draft", "invalid_transition",
            "if_match_required", "preview_in_progress", "preview_missing", "preview_stale", "justification_required",
            "rebaseline_reason_required", "profile_invalid", "sod_author_cannot_approve", "sod_business_reviewer",
            "approver_permission_not_explicit", "approver_role_required", "sod_self_granted_approver", "committee_ref_required",
            "committee_attachment_required", "board_ref_required", "rebaseline_confirmation_required", "reason_required",
            "file_not_owned", "file_already_attached", "profile_values_require_global_scope",
            "field_not_supported_for_type", "criticality_out_of_range", "classification_not_found", "justification_too_short",
            "empty_batch", "duplicate_entity_in_batch", "entity_not_found", "risk_context_requires_global_scope",
            "risk_context_requires_permission", "entity_history_requires_permission"
        ];
        AssertDeclaredEverywhere(codes.Select(RiskApiErrorText.ResourceKey));
    }

    /// <summary>The keys the new screens name in code (gates, helpers, view-models) — a missing one renders as the key itself.</summary>
    [Fact]
    public void TestEveryKeyTheConfigurationScreensNameExists()
    {
        var root = RiskIndexTestText.ClientDirectory();
        var sources = Directory.GetFiles(Path.Combine(root, "ViewModels", "Admin", "RiskIndex"), "*.cs")
            .Concat(new[] { "ProfileWorkflowGates.cs", "RiskIndexAdminAccess.cs", "RiskContextDraft.cs", "RiskSourceCatalogue.cs" }
                .Select(f => Path.Combine(root, "Tools", "RiskIndex", f)));
        var keys = sources.SelectMany(f => Regex.Matches(File.ReadAllText(f),
                    @"""(?<key>(?:RiskAdmin|RiskContext|RiskSources?|RiskPreview|RiskApprovalCheck|RiskPublication|RiskRebaseline|RiskPreset|RiskParam|RiskIssue|AdminRiskIndex)[A-Za-z0-9_]*)""")
                .Select(m => m.Groups["key"].Value))
            .Where(k => !k.EndsWith('_') && k != nameof(RiskContextDraft))
            .Distinct()
            .ToList();

        Assert.NotEmpty(keys);
        AssertDeclaredEverywhere(keys);
    }

    [Fact]
    public void TestAnIssueReadsAsASentenceWithItsNumbersAndTheParameterLabel()
    {
        var issue = new RiskIndexValidationIssueDto("parameter_out_of_range", "p",
            new Dictionary<string, string> { ["value"] = "20", ["min"] = "2", ["max"] = "16" });

        var english = ProfileIssueText.Line(issue, RiskIndexTestText.English);
        var portuguese = ProfileIssueText.Line(issue, RiskIndexTestText.Portuguese);

        Assert.StartsWith(RiskIndexTestText.English(ProfileParameterRows.LabelKey("p"))!, english);
        Assert.Contains("20", english);
        Assert.Contains("16", english);
        Assert.Contains("16", portuguese);
        Assert.NotEqual(english, portuguese);
    }

    [Fact]
    public void TestAMapEntryBorrowsItsCompositesLabelAndAnUnknownCodeFallsBackToItself()
    {
        Assert.Equal(RiskIndexTestText.English("RiskParam_u_Label") + " · EXP.D",
            ProfileIssueText.Label("u.EXP.D", RiskIndexTestText.English));
        Assert.Null(ProfileIssueText.Label("nothing", RiskIndexTestText.English));

        var unknown = new RiskIndexValidationIssueDto("brand_new_rule", null, new Dictionary<string, string>());
        Assert.Equal("brand_new_rule", ProfileIssueText.Message(unknown, RiskIndexTestText.English));
    }

    [Fact]
    public void TestABandsIssueReadsItsReason()
    {
        var issue = new RiskIndexValidationIssueDto("bands_invalid", "faixas",
            new Dictionary<string, string> { ["reason"] = "not_increasing", ["index"] = "2" });

        Assert.Equal("RiskIssue_bands_invalid_not_increasing", ProfileIssueText.ResourceKey(issue));
        Assert.Contains("2", ProfileIssueText.Message(issue, RiskIndexTestText.Portuguese));
    }
}
