using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using DAL.Enums;
using GUIClient.Agents;
using GUIClient.Agents.EditTools;
using GUIClient.Agents.ReadTools;
using GUIClient.Tools.RiskIndex;
using JetBrains.Annotations;
using Model.Assessments;
using Model.Authentication;
using Model.DataCatalogue;
using Model.DTO;
using Model.Entities;
using Model.RiskFlags;
using Model.RiskIndex;
using NSubstitute;
using Xunit;

namespace GUIClient.Tests.Agents;

/// <summary>
/// T362 (S55, S52): an item whose data class the organisation withholds from agents is never listed, never returned by id
/// and never edited; a search reveals only a count, independent of what a withheld item says; items outside the excluded
/// classes are unaffected; and whatever cannot be classified is withheld — S52 D4, "absent is never compliant".
/// </summary>
[TestSubject(typeof(AgentClassificationScope))]
public class AgentDataClassExclusionTest
{
    // ---- excluded items, by every read tool -------------------------------------------------------------------------

    /// <summary>Every read tool asked by id for a withheld item (each a different reason it is withheld).</summary>
    public static TheoryData<string, string> WithheldReads() => new()
    {
        { "get_risk", "{\"risk_id\":2}" },                                   // linked to a sensitive data record
        { "get_risk", "{\"risk_id\":3}" },                                   // flag 5, no entity
        { "get_risk", "{\"risk_id\":4}" },                                   // linked to an uncatalogued data record
        { "get_risk_mitigation", "{\"risk_id\":2}" },
        { "list_risk_reviews", "{\"risk_id\":2}" },
        { "get_finding", "{\"finding_id\":2}" },                             // through its host's application
        { "get_finding", "{\"finding_id\":3}" },                             // under a classified application
        { "get_finding_lifecycle", "{\"finding_id\":2}" },
        { "get_incident", "{\"incident_id\":2}" },
        { "list_assessment_runs", "{\"assessment_id\":2}" },
        { "get_assessment_run_answers", "{\"assessment_id\":1,\"run_id\":2}" },
        { "get_assessment_run_answers", "{\"assessment_id\":2,\"run_id\":3}" }, // a visible run of a withheld assessment
        { "list_assessment_evidence", "{\"assessment_id\":1,\"run_id\":2}" },
        { "get_entity", "{\"entity_id\":20}" },                              // classified at a sensitive level (S46)
        { "get_entity", "{\"entity_id\":21}" },                              // inherited from its parent
        { "get_entity", "{\"entity_id\":30}" },                              // catalogued sensitive personal data (S52)
        { "get_entity", "{\"entity_id\":32}" },                              // not catalogued: fails closed
        { "get_entity_relationships", "{\"entity_id\":20}" },
        { "get_host", "{\"host_id\":2}" },
        { "get_risk_overview", "{\"node_ref\":\"e:20\"}" },
        { "list_risk_overview_children", "{\"node_ref\":\"e:20\"}" }
    };

    [Theory]
    [MemberData(nameof(WithheldReads))]
    public async Task TestAnExcludedItemIsNeverReturnedByAnyReadTool(string tool, string arguments)
    {
        var world = new ClassifiedWorld();
        var answer = await world.Rig().CallAsync(tool, JsonNode.Parse(arguments)!.AsObject());

        Assert.True(answer.IsError, answer.Body);
        Assert.Equal(AgentToolErrors.Withheld, answer.Status);
        Assert.DoesNotContain(ClassifiedWorld.Marker, answer.Body, StringComparison.Ordinal);
    }

    /// <summary>Every search and list, with the ids it may show and how many it withholds.</summary>
    public static TheoryData<string, string, string, int> Searches() => new()
    {
        { "search_risks", "{}", "1", 4 },
        { "search_findings", "{}", "1", 3 },
        { "search_incidents", "{}", "1", 1 },
        { "list_assessments", "{}", "1", 1 },
        { "list_assessment_runs", "{\"assessment_id\":1}", "1", 1 },
        { "search_entities", "{}", "10,11,12,22,31,33,40", 4 },
        { "search_hosts", "{}", "1", 1 },
        { "get_entity_relationships", "{\"entity_id\":10}", "11,12,22,31,33,40", 3 },
        { "list_risk_overview_children", "{}", "e:22,u", 1 }
    };

    [Theory]
    [MemberData(nameof(Searches))]
    public async Task TestEverySearchWithholdsExcludedItemsAndCountsThem(string tool, string arguments, string visible,
        int withheld)
    {
        var world = new ClassifiedWorld();
        var answer = await world.Rig().CallAsync(tool, JsonNode.Parse(arguments)!.AsObject());

        Assert.False(answer.IsError, answer.Body);
        Assert.DoesNotContain(ClassifiedWorld.Marker, answer.Body, StringComparison.Ordinal);
        Assert.Equal(withheld, (int)answer.Structured["withheld_classified_items"]!);
        Assert.Equal(visible.Split(','), ListedIds(answer));
    }

    [Theory]
    [InlineData("search_risks", 4)]
    [InlineData("search_incidents", 1)]
    [InlineData("list_assessments", 1)]
    [InlineData("search_entities", 4)]
    public async Task TestASearchCountDoesNotDependOnWhatAWithheldItemSays(string tool, int withheld)
    {
        // The count is taken before the query is matched, so it is the same whatever is asked: text found only in withheld
        // items lists nothing, text found only in a visible one lists it, and neither moves the count.
        var rig = new ClassifiedWorld().Rig();
        var hidden = await rig.CallAsync(tool, new JsonObject { ["query"] = ClassifiedWorld.Marker });
        var visible = await rig.CallAsync(tool, new JsonObject { ["query"] = "Portal" });

        Assert.False(hidden.IsError, hidden.Body);
        Assert.Empty(ListedIds(hidden));
        Assert.Single(ListedIds(visible));
        Assert.Equal(withheld, (int)hidden.Structured["withheld_classified_items"]!);
        Assert.Equal(withheld, (int)visible.Structured["withheld_classified_items"]!);
    }

    [Fact]
    public async Task TestAVisibleEntityNeverNamesAWithheldOne()
    {
        // Admissions (40) points at the health records (30, withheld) and the public catalogue (31) through 'data'.
        var answer = await new ClassifiedWorld().Rig().CallAsync("get_entity", new JsonObject { ["entity_id"] = 40 });

        Assert.False(answer.IsError, answer.Body);
        var data = answer.Data!["properties"]!["items"]!.AsArray()
            .Where(p => (string?)p!["name"] == "data").Select(p => (string?)p!["value"]).ToList();
        Assert.Equal(new[] { "31" }, data);
        Assert.Equal(1, (int)answer.Structured["withheld_classified_items"]!);
    }

    [Theory]
    [InlineData("get_risk_overview", "{\"node_ref\":\"e:22\"}")]
    [InlineData("get_risk_overview", "{}")]
    public async Task TestTheOverviewPathNeverNamesAWithheldNode(string tool, string arguments)
    {
        var answer = await new ClassifiedWorld().Rig().CallAsync(tool, JsonNode.Parse(arguments)!.AsObject());

        Assert.False(answer.IsError, answer.Body);
        Assert.DoesNotContain(ClassifiedWorld.Marker, answer.Body, StringComparison.Ordinal);
        Assert.Equal(1, (int)answer.Structured["withheld_classified_items"]!); // e:20 in the breadcrumb
    }

    // ---- excluded items, by every edit tool -------------------------------------------------------------------------

    public static TheoryData<string> EditTools() => AgentEditFixtures.AllTools();

    [Theory]
    [MemberData(nameof(EditTools))]
    public async Task TestNoEditToolTouchesAnExcludedItem(string tool)
    {
        var world = new ClassifiedWorld();
        var answer = await world.Rig().CallAsync(tool, world.EditArguments(tool, excluded: true));

        Assert.True(answer.IsError, answer.Body);
        Assert.Equal(AgentToolErrors.Withheld, answer.Status);
        Assert.DoesNotContain(ClassifiedWorld.Marker, answer.Body, StringComparison.Ordinal);
        world.AssertNothingWritten();
    }

    // ---- included items are unaffected ------------------------------------------------------------------------------

    public static TheoryData<string, string> VisibleReads() => new()
    {
        { "get_risk", "{\"risk_id\":1}" },
        { "get_risk_mitigation", "{\"risk_id\":1}" },
        { "list_risk_reviews", "{\"risk_id\":1}" },
        { "get_finding", "{\"finding_id\":1}" },
        { "get_finding_lifecycle", "{\"finding_id\":1}" },
        { "get_incident", "{\"incident_id\":1}" },
        { "list_assessment_runs", "{\"assessment_id\":1}" },
        { "get_assessment_run_answers", "{\"assessment_id\":1,\"run_id\":1}" },
        { "list_assessment_evidence", "{\"assessment_id\":1,\"run_id\":1}" },
        { "get_entity", "{\"entity_id\":22}" },
        { "get_entity", "{\"entity_id\":31}" },
        { "get_entity", "{\"entity_id\":33}" },
        { "get_entity_relationships", "{\"entity_id\":22}" },
        { "get_host", "{\"host_id\":1}" },
        { "get_risk_overview", "{\"node_ref\":\"e:22\"}" },
        { "list_risk_overview_children", "{\"node_ref\":\"e:22\"}" }
    };

    [Theory]
    [MemberData(nameof(VisibleReads))]
    public async Task TestAnIncludedItemIsReturnedAsBefore(string tool, string arguments)
    {
        var answer = await new ClassifiedWorld().Rig().CallAsync(tool, JsonNode.Parse(arguments)!.AsObject());

        Assert.False(answer.IsError, answer.Body);
        Assert.Equal("ok", answer.Status);
        Assert.DoesNotContain(ClassifiedWorld.Marker, answer.Body, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EditTools))]
    public async Task TestAnEditToolStillWritesAnIncludedItem(string tool)
    {
        var world = new ClassifiedWorld();
        var answer = await world.Rig().CallAsync(tool, world.EditArguments(tool, excluded: false));

        Assert.NotEqual(AgentToolErrors.Withheld, answer.Status);
        Assert.True(world.Writes() > 0, answer.Body);
    }

    [Fact]
    public async Task TestWithNothingExcludedEveryItemIsReturned()
    {
        var world = new ClassifiedWorld { Policy = AgentExclusionPolicy.None };
        var rig = world.Rig();

        Assert.Equal("ok", (await rig.CallAsync("get_risk", new JsonObject { ["risk_id"] = 2 })).Status);
        Assert.Equal("ok", (await rig.CallAsync("get_entity", new JsonObject { ["entity_id"] = 32 })).Status);
        var search = await rig.CallAsync("search_risks", new JsonObject());
        Assert.Equal(0, (int)search.Structured["withheld_classified_items"]!);
        Assert.Equal(5, ListedIds(search).Count);
        Assert.Equal(0, world.ClassificationReads); // an inactive policy classifies nothing
    }

    [Fact]
    public async Task TestOnlyTheExcludedClassesAreWithheld()
    {
        // Personal data plus the floor: the contacts record (33) goes, and so does sensitive personal data (30, the floor);
        // the sensitive security level (20) stays, because that class is not excluded now. The uncatalogued record (32)
        // still goes: unknown is every class.
        var world = new ClassifiedWorld { Policy = AgentExclusionPolicy.Of(new[] { AgentDataClass.PersonalData }) };
        var rig = world.Rig();

        Assert.Equal(AgentToolErrors.Withheld, (await rig.CallAsync("get_entity", new JsonObject { ["entity_id"] = 33 })).Status);
        Assert.Equal(AgentToolErrors.Withheld, (await rig.CallAsync("get_entity", new JsonObject { ["entity_id"] = 32 })).Status);
        Assert.Equal(AgentToolErrors.Withheld, (await rig.CallAsync("get_entity", new JsonObject { ["entity_id"] = 30 })).Status);
        Assert.Equal("ok", (await rig.CallAsync("get_entity", new JsonObject { ["entity_id"] = 20 })).Status);
    }

    // ---- fails closed -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task TestAnUnreadableCatalogueWithholdsEveryDataRecordAndWhatLinksToOne()
    {
        // A user who cannot read GET /DataCatalogue/Records (403): no data record can be classified.
        var world = new ClassifiedWorld();
        world.Catalogue.GetRecordsAsync(Arg.Any<bool>()).Returns<Task<List<DataRecordSummaryDto>>>(_ =>
            throw new HttpRequestException("Forbidden", null, System.Net.HttpStatusCode.Forbidden));
        var rig = world.Rig();

        Assert.Equal(AgentToolErrors.Withheld, (await rig.CallAsync("get_entity", new JsonObject { ["entity_id"] = 31 })).Status);
        Assert.Equal(AgentToolErrors.Withheld, (await rig.CallAsync("get_incident", new JsonObject { ["incident_id"] = 1 })).Status);
        Assert.Equal("ok", (await rig.CallAsync("get_entity", new JsonObject { ["entity_id"] = 22 })).Status);
    }

    [Fact]
    public async Task TestAnUnreadableEntityListWithholdsEveryLinkedItem()
    {
        var world = new ClassifiedWorld { FailEntities = true };
        var rig = world.Rig();

        Assert.Equal(AgentToolErrors.Withheld, (await rig.CallAsync("get_host", new JsonObject { ["host_id"] = 1 })).Status);
        Assert.Equal(AgentToolErrors.Withheld, (await rig.CallAsync("get_finding", new JsonObject { ["finding_id"] = 1 })).Status);
        Assert.Equal(AgentToolErrors.Withheld,
            (await rig.CallAsync("update_host_fields", world.EditArguments("update_host_fields", excluded: false))).Status);
        world.AssertNothingWritten();
    }

    [Fact]
    public async Task TestUnreadableRiskFlagsWithholdEveryRisk()
    {
        var world = new ClassifiedWorld();
        world.Flags.GetFlaggedAsync(Arg.Any<RiskFlagCode?>(), Arg.Any<bool?>()).Returns<Task<List<FlaggedRiskDto>>>(_ =>
            throw new HttpRequestException("Server error", null, System.Net.HttpStatusCode.InternalServerError));
        var rig = world.Rig();

        Assert.Equal(AgentToolErrors.Withheld, (await rig.CallAsync("get_risk", new JsonObject { ["risk_id"] = 1 })).Status);
        var search = await rig.CallAsync("search_risks", new JsonObject());
        Assert.Empty(ListedIds(search));
        Assert.Equal(5, (int)search.Structured["withheld_classified_items"]!);
    }

    // ---- T359 review, blocker 1: how the GUI links a risk (and a finding through it) ---------------------------------

    [Theory]
    [InlineData("get_risk", "{\"risk_id\":5}")]
    [InlineData("get_risk_mitigation", "{\"risk_id\":5}")]
    [InlineData("list_risk_reviews", "{\"risk_id\":5}")]
    [InlineData("get_finding", "{\"finding_id\":4}")]
    [InlineData("get_finding_lifecycle", "{\"finding_id\":4}")]
    public async Task TestAnItemLinkedOnlyThroughTheGuiRiskAssociationIsWithheld(string tool, string arguments)
    {
        // Risk 5 has no EntityId: the GUI's "Entity" field wrote risk_to_entity, which reaches the client only as the
        // chain's Legacy link to the health records (30). Finding 4 names no entity or host, only risk 5.
        var answer = await new ClassifiedWorld().Rig().CallAsync(tool, JsonNode.Parse(arguments)!.AsObject());

        Assert.Equal(AgentToolErrors.Withheld, answer.Status);
        Assert.DoesNotContain(ClassifiedWorld.Marker, answer.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("search_risks", "5")]
    [InlineData("search_findings", "4")]
    public async Task TestASearchDropsAndCountsAnItemLinkedThroughTheGuiRiskAssociation(string tool, string hidden)
    {
        var answer = await new ClassifiedWorld().Rig().CallAsync(tool, new JsonObject());

        Assert.DoesNotContain(hidden, ListedIds(answer));
        Assert.True((int)answer.Structured["withheld_classified_items"]! >= 1);
        Assert.DoesNotContain(ClassifiedWorld.Marker, answer.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("update_risk", "{\"risk_id\":5,\"version\":\"rev-0\",\"notes\":\"x\"}")]
    [InlineData("update_finding_status", "{\"finding_id\":4,\"version\":\"rev-0\",\"status\":\"verified\"}")]
    [InlineData("add_finding_note", "{\"finding_id\":4,\"note\":\"x\"}")]
    public async Task TestNoEditToolTouchesAnItemLinkedThroughTheGuiRiskAssociation(string tool, string arguments)
    {
        var world = new ClassifiedWorld();
        var answer = await world.Rig().CallAsync(tool, JsonNode.Parse(arguments)!.AsObject());

        Assert.Equal(AgentToolErrors.Withheld, answer.Status);
        world.AssertNothingWritten();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task TestARiskWhoseChainCannotBeReadIsWithheld(bool failChains, bool redactedHost)
    {
        // A chain read refused or failed, or a host link hidden from this user: the risk cannot be classified.
        var world = new ClassifiedWorld { FailChains = failChains };
        if (redactedHost) world.ChainLinks[1] = new[] { new Model.Risks.Chain.RiskChainLinkDto { Id = 9, RiskId = 1, IsRedacted = true, TargetType = "host" } };
        var rig = world.Rig();

        Assert.Equal(AgentToolErrors.Withheld, (await rig.CallAsync("get_risk", new JsonObject { ["risk_id"] = 1 })).Status);
        var search = await rig.CallAsync("search_risks", new JsonObject());
        Assert.DoesNotContain("1", ListedIds(search));
    }

    [Fact]
    public async Task TestADispatcherWithoutAClassificationWithholds()
    {
        // Forgetting to wire the source must not leak: the default is the default policy over a source that answers nothing.
        Assert.Same(AgentClassificationOptions.Unconfigured, new AgentCoworkOptions().Classification);
        Assert.Same(AgentClassificationOptions.Unconfigured,
            new AgentToolCall(null!, null!).ClassificationOptions);

        var world = new ClassifiedWorld();
        var rig = AgentReadRig.OverTools(world.Tools(), AgentClassificationOptions.Unconfigured, AgentReadRig.Everything());
        Assert.Equal(AgentToolErrors.Withheld, (await rig.CallAsync("get_risk", new JsonObject { ["risk_id"] = 1 })).Status);
        Assert.Equal(AgentToolErrors.Withheld, (await rig.CallAsync("get_entity", new JsonObject { ["entity_id"] = 22 })).Status);
        Assert.Equal("ok", (await rig.CallAsync("whoami", new JsonObject())).Status); // nothing to classify
    }

    [Fact]
    public void TestAnUnreadablePolicyWithholdsEveryClass()
    {
        Assert.Same(AgentExclusionPolicy.Default, AgentExclusionPolicy.Parse(null));
        Assert.Equal(AgentDataClasses.All, AgentExclusionPolicy.Parse("sensitive_personal_data,whatever").Excluded);
        // Only "" means "nothing beyond the floor": an empty entry is unreadable (it used to read as "none").
        Assert.Equal(AgentDataClasses.All, AgentExclusionPolicy.Parse(",").Excluded);
        Assert.Equal(AgentDataClasses.All, AgentExclusionPolicy.Parse("personal_data,").Excluded);
        Assert.Equal(AgentDataClasses.All, new AgentClassificationScope(
            new AgentClassificationOptions(UnavailableClassificationSource.Instance, () => throw new FormatException()),
            new AgentWithholding()).Policy.Excluded);
        var roundTrip = AgentExclusionPolicy.Parse(AgentExclusionPolicy.Default.Serialize());
        Assert.Equal(AgentExclusionPolicy.Default.Excluded, roundTrip.Excluded);
        Assert.Equal(new[] { AgentDataClass.SensitivePersonalData, AgentDataClass.ChildrenData, AgentDataClass.SensitiveClassification },
            AgentExclusionPolicy.Default.Excluded.OrderBy(c => c));
    }

    /// <summary>T359 review #2c: no stored value and no choice in the window goes below the organisation minimum.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("personal_data")]
    [InlineData("sensitive_classification")]
    [InlineData(null)]
    public void TestAStoredPolicyCannotGoBelowTheFloor(string? stored)
    {
        Assert.Equal(new[] { AgentDataClass.SensitivePersonalData, AgentDataClass.ChildrenData }, AgentExclusionPolicy.Floor.OrderBy(c => c));
        Assert.Superset(AgentExclusionPolicy.Floor.ToHashSet(), AgentExclusionPolicy.Parse(stored).Excluded.ToHashSet());
        Assert.Superset(AgentExclusionPolicy.Floor.ToHashSet(), AgentExclusionPolicy.Of(Array.Empty<AgentDataClass>()).Excluded.ToHashSet());
        Assert.Superset(AgentExclusionPolicy.Floor.ToHashSet(),
            AgentExclusionPolicy.Parse(AgentExclusionPolicy.Parse(stored).Serialize()).Excluded.ToHashSet());
    }

    [Fact]
    public async Task TestAnEmptyStoredPolicyStillWithholdsSensitivePersonalData()
    {
        var world = new ClassifiedWorld { Policy = AgentExclusionPolicy.Parse("") };
        var rig = world.Rig();

        Assert.Equal(AgentToolErrors.Withheld, (await rig.CallAsync("get_entity", new JsonObject { ["entity_id"] = 30 })).Status);
        Assert.Equal(AgentToolErrors.Withheld, (await rig.CallAsync("get_risk", new JsonObject { ["risk_id"] = 2 })).Status);
    }

    [Fact]
    public void TestDuplicateCatalogueRowsAreUnitedNotPicked()
    {
        var entities = new List<Entity> { Entity(1, "organizationData", "Records", null) };
        var rows = new List<DataRecordSummaryDto>
        {
            new() { EntityId = 1, Catalogued = true, PersonalData = PersonalDataCategory.NotPersonal, InvolvesMinors = false, LargeVolume = false, StrategicResearch = false },
            new() { EntityId = 1, Catalogued = true, PersonalData = PersonalDataCategory.SensitivePersonal, InvolvesMinors = false, LargeVolume = false, StrategicResearch = false }
        };

        Assert.Contains(AgentDataClass.SensitivePersonalData, AgentEntityClassifier.Classify(entities, rows)[1]);
        rows.Reverse();
        Assert.Contains(AgentDataClass.SensitivePersonalData, AgentEntityClassifier.Classify(entities, rows)[1]);
        rows.Add(new DataRecordSummaryDto { EntityId = 1, Catalogued = false });
        Assert.Equal(AgentDataClasses.All, AgentEntityClassifier.Classify(entities, rows)[1]);
    }

    [Fact]
    public void TestTheClassifierFailsClosedOnWhatItCannotRead()
    {
        var level = Entity(1, "securityClassificationLevel", "Odd", null, ("sensitive", "maybe"));
        var entities = new List<Entity>
        {
            level,
            Entity(2, "application", "Dangling level", null, ("securityClassification", "999")),
            Entity(3, "application", "Odd level", null, ("securityClassification", "1")),
            Entity(4, "organizationData", "Category undeclared", null),
            Entity(5, "organizationData", "Minors undeclared", null),
            Entity(6, "applicationModule", "Orphan", 77),
            Entity(7, "application", "Blank classification", null, ("securityClassification", " "))
        };
        var records = new List<DataRecordSummaryDto>
        {
            new() { EntityId = 4, Catalogued = true, PersonalData = null },
            new() { EntityId = 5, Catalogued = true, PersonalData = PersonalDataCategory.NotPersonal, InvolvesMinors = null,
                LargeVolume = false, StrategicResearch = false }
        };

        var classes = AgentEntityClassifier.Classify(entities, records);

        Assert.Equal(AgentDataClasses.All, classes[2]);
        Assert.Equal(AgentDataClasses.All, classes[3]);
        Assert.Equal(AgentDataClasses.All, classes[4]);
        Assert.Equal(new[] { AgentDataClass.ChildrenData }, classes[5]);
        Assert.Equal(AgentDataClasses.All, classes[6]);
        Assert.Empty(classes[7]);
        Assert.Equal(AgentDataClasses.All, AgentEntityClassifier.Classify(entities, null)[5]); // catalogue unreadable
    }

    // ---- who changes the policy -------------------------------------------------------------------------------------

    [Fact]
    public void TestOnlyTheCatalogueWriterMayChangeThePolicy()
    {
        Assert.False(AgentExclusionPolicyAccess.CanChange(null));
        Assert.False(AgentExclusionPolicyAccess.CanChange(AgentRig.DefaultUser("riskmanagement", "entities_manage")));
        Assert.True(AgentExclusionPolicyAccess.CanChange(AgentRig.DefaultUser("data_catalogue_manage")));
        Assert.False(AgentExclusionPolicyAccess.CanChange(AgentRig.DefaultUser("data_catalogue_manage",
            Model.DecisionCycle.ThirdLineAssurance.PermissionKey)));
        var admin = AgentRig.DefaultUser("riskmanagement");
        admin.IsAdmin = true;
        Assert.Equal(GUIClient.Tools.AdminRoleClaim.IsHeldBy(admin), AgentExclusionPolicyAccess.CanChange(admin));
    }

    [Fact]
    public async Task TestNoAgentCallCanChangeThePolicy()
    {
        // No reviewed tool names the policy, and an argument naming it is never read: the policy is the runtime's.
        Assert.DoesNotContain(AgentToolInventory.Reviewed.Keys, name =>
            System.Text.RegularExpressions.Regex.IsMatch(name, "classif|exclu|polic|setting|catalogue"));

        var world = new ClassifiedWorld();
        var rig = world.Rig();
        var answer = await rig.CallAsync("get_risk", new JsonObject
        {
            ["risk_id"] = 2, ["excluded_data_classes"] = new JsonArray(), ["policy"] = "none"
        });
        Assert.Equal(AgentToolErrors.Withheld, answer.Status);
        Assert.Same(AgentExclusionPolicy.Default, world.Policy);
    }

    [Fact]
    public async Task TestTheActivityPanelShowsAWithheldCallAsSuch()
    {
        var world = new ClassifiedWorld();
        var rig = world.Rig();
        await rig.CallAsync("get_risk", new JsonObject { ["risk_id"] = 2 });

        Assert.Contains(rig.Rig.Activity.Entries(), e => e.Tool == "get_risk" && e.Result == AgentToolErrors.Withheld);
    }

    /// <summary>T361: every class label the Agents window and the access dialog compute exists in every shipped culture.</summary>
    [Theory]
    [InlineData("Localization.resx")]
    [InlineData("Localization.en-US.resx")]
    [InlineData("Localization.pt-BR.resx")]
    public void TestEveryClassLabelResolvesIn(string file)
    {
        var declared = System.Xml.Linq.XDocument.Load(GUIClient.Tests.Resources.EntityConfigurationSchema.ResourcePath(file)).Root!
            .Elements("data").Select(d => d.Attribute("name")?.Value).ToHashSet(StringComparer.Ordinal);

        var missing = AgentDataClasses.All.Select(AgentDataClasses.LabelKey).Where(k => !declared.Contains(k)).ToList();

        Assert.True(missing.Count == 0, $"{file} does not declare: {string.Join(", ", missing)}");
    }

    // ---- helpers ----------------------------------------------------------------------------------------------------

    private static List<string> ListedIds(ToolAnswer answer)
    {
        var data = answer.Data!.AsObject();
        JsonArray items = data["items"] is JsonArray direct ? direct
            : data["children"]?["items"] is JsonArray children ? children
            : data["runs"]?["items"] as JsonArray ?? data["rows"]?["items"] as JsonArray ?? new JsonArray();
        return items.Select(i => i!["id"] is { } id ? id.ToString() : (string)i["node"]!["node_ref"]!).ToList();
    }

    internal static Entity Entity(int id, string definition, string name, int? parent, params (string Type, string Value)[] properties) =>
        new()
        {
            Id = id, DefinitionName = definition, Status = "Active", Parent = parent, Updated = new DateTime(2026, 5, 1),
            EntitiesProperties = new[] { ("name", name) }.Concat(properties)
                .Select((p, i) => new EntitiesProperty { Id = id * 100 + i, Type = p.Item1, Name = p.Item1 + "-" + id, Value = p.Item2 })
                .ToList()
        };
}

/// <summary>
/// A small organisation with every reason an item is withheld: a sensitive security level (S46), catalogued sensitive
/// personal data (S52), an uncatalogued data record, flag 5 on a risk, inheritance from a parent, and a host's entity.
/// Withheld items carry <see cref="Marker"/> in their text; an answer that contains it leaked.
/// </summary>
internal sealed class ClassifiedWorld
{
    public const string Marker = "SECRET";

    public AgentExclusionPolicy Policy { get; set; } = AgentExclusionPolicy.Default;
    public bool FailEntities { get; set; }
    public bool FailChains { get; set; }

    /// <summary>Each risk's chain links, as GET /RiskChain/Risks/{id} returns them (the GUI's "Entity" field is a Legacy link).</summary>
    public Dictionary<int, Model.Risks.Chain.RiskChainLinkDto[]> ChainLinks { get; } = new()
    {
        [1] = new[] { Link(1, 22) },
        [5] = new[] { Link(5, 30) }
    };

    private static Model.Risks.Chain.RiskChainLinkDto Link(int riskId, int entityId) => new()
    {
        Id = riskId * 10, RiskId = riskId, EntityId = entityId, TargetType = "organizationData",
        Origin = RiskChainLinkOrigin.Legacy
    };
    public int ClassificationReads;

    public List<Entity> Entities { get; } = new()
    {
        AgentDataClassExclusionTest.Entity(10, "organization", "Org", null),
        AgentDataClassExclusionTest.Entity(11, "securityClassificationLevel", "Restricted", 10, ("sensitive", "True")),
        AgentDataClassExclusionTest.Entity(12, "securityClassificationLevel", "Internal", 10, ("sensitive", "False")),
        AgentDataClassExclusionTest.Entity(20, "application", Marker + " clinic", 10, ("securityClassification", "11")),
        AgentDataClassExclusionTest.Entity(21, "applicationModule", Marker + " module", 20),
        AgentDataClassExclusionTest.Entity(22, "application", "Portal", 10, ("securityClassification", "12")),
        AgentDataClassExclusionTest.Entity(30, "organizationData", Marker + " health records", 10),
        AgentDataClassExclusionTest.Entity(31, "organizationData", "Public catalogue", 10),
        AgentDataClassExclusionTest.Entity(32, "organizationData", Marker + " uncatalogued", 10),
        AgentDataClassExclusionTest.Entity(33, "organizationData", "Contacts", 10),
        AgentDataClassExclusionTest.Entity(40, "businessProcess", "Admissions", 10, ("data", "30"), ("data", "31"))
    };

    public List<DataRecordSummaryDto> Records { get; } = new()
    {
        new() { EntityId = 30, Catalogued = true, PersonalData = PersonalDataCategory.SensitivePersonal, InvolvesMinors = false, LargeVolume = false, StrategicResearch = false },
        new() { EntityId = 31, Catalogued = true, PersonalData = PersonalDataCategory.NotPersonal, InvolvesMinors = false, LargeVolume = false, StrategicResearch = false },
        new() { EntityId = 32, Catalogued = false },
        new() { EntityId = 33, Catalogued = true, PersonalData = PersonalDataCategory.Personal, InvolvesMinors = false, LargeVolume = false, StrategicResearch = false }
    };

    public List<Risk> Risks { get; } = new()
    {
        new() { Id = 1, Subject = "Portal outage", Status = "New", EntityId = 22, LastUpdate = new DateTime(2026, 2, 1) },
        new() { Id = 2, Subject = Marker + " risk on health data", Status = "New", EntityId = 30, LastUpdate = new DateTime(2026, 2, 2) },
        new() { Id = 3, Subject = Marker + " flagged risk", Status = "New", LastUpdate = new DateTime(2026, 2, 3) },
        new() { Id = 4, Subject = Marker + " risk on an uncatalogued record", Status = "New", EntityId = 32, LastUpdate = new DateTime(2026, 2, 4) },
        // Linked in the GUI: EntityId stays null, the association lives in risk_to_entity and the chain (ChainLinks[5]).
        new() { Id = 5, Subject = Marker + " risk linked through the Entity field", Status = "New", LastUpdate = new DateTime(2026, 2, 5) }
    };

    public List<Vulnerability> Findings { get; } = new()
    {
        new() { Id = 1, Title = "Portal XSS", EntityId = 22, LifecycleStatus = FindingStatus.Active, FixTeamId = 2 },
        new() { Id = 2, Title = Marker + " clinic host finding", HostId = 2, LifecycleStatus = FindingStatus.Active, FixTeamId = 2 },
        new() { Id = 3, Title = Marker + " module finding", EntityId = 21, LifecycleStatus = FindingStatus.Active, FixTeamId = 2 },
        new() { Id = 4, Title = Marker + " finding linked only to risk 5", LifecycleStatus = FindingStatus.Active, FixTeamId = 2 }
    };

    public List<Host> Hosts { get; } = new()
    {
        new() { Id = 1, HostName = "portal01", EntityId = 22, Comment = "dmz" },
        new() { Id = 2, HostName = Marker + "-clinic01", EntityId = 20, Comment = Marker }
    };

    public List<Incident> Incidents { get; } = new()
    {
        new() { Id = 1, Name = "Portal phishing", Description = "Mail", ImpactedEntityId = 31, ReportDate = new DateTime(2026, 4, 1) },
        new() { Id = 2, Name = Marker + " incident", Description = Marker, EntityId = 30, ReportDate = new DateTime(2026, 4, 2) }
    };

    public List<Assessment> Assessments { get; } = new()
    {
        new() { Id = 1, Name = "Portal review", EntityId = 22 },
        new() { Id = 2, Name = Marker + " assessment", EntityId = 30 }
    };

    public List<AssessmentRun> Runs { get; } = new()
    {
        new() { Id = 1, AssessmentId = 1, EntityId = 22, Comments = "Portal run" },
        new() { Id = 2, AssessmentId = 1, EntityId = 20, Comments = Marker + " run" },
        new() { Id = 3, AssessmentId = 2, EntityId = 22, Comments = Marker + " run of a withheld assessment" }
    };

    public IRisksService RisksService { get; } = Substitute.For<IRisksService>();
    public IMitigationService Mitigations { get; } = Substitute.For<IMitigationService>();
    public IVulnerabilitiesService FindingsService { get; } = Substitute.For<IVulnerabilitiesService>();
    public IIncidentsService IncidentsService { get; } = Substitute.For<IIncidentsService>();
    public IIncidentResponsePlansService Plans { get; } = Substitute.For<IIncidentResponsePlansService>();
    public IAssessmentsService AssessmentsService { get; } = Substitute.For<IAssessmentsService>();
    public IAssessmentEvidenceService Evidence { get; } = Substitute.For<IAssessmentEvidenceService>();
    public IEntitiesService EntitiesService { get; } = Substitute.For<IEntitiesService>();
    public IHostsService HostsService { get; } = Substitute.For<IHostsService>();
    public ITeamsService Teams { get; } = Substitute.For<ITeamsService>();
    public IRiskIndexService RiskIndex { get; } = Substitute.For<IRiskIndexService>();
    public IDataCatalogueService Catalogue { get; } = Substitute.For<IDataCatalogueService>();
    public IRiskFlagsService Flags { get; } = Substitute.For<IRiskFlagsService>();
    public IRiskChainService Chains { get; } = Substitute.For<IRiskChainService>();

    public ClassifiedWorld()
    {
        RisksService.GetAllRisksAsync(Arg.Any<bool>()).Returns(_ => Task.FromResult(Risks.ToList()));
        Findings.First(f => f.Id == 4).Risks = new List<Risk> { Risks.First(r => r.Id == 5) };
        Chains.GetRiskChainAsync(Arg.Any<int>()).Returns(c =>
        {
            if (FailChains) throw new HttpRequestException("Forbidden", null, System.Net.HttpStatusCode.Forbidden);
            var links = ChainLinks.TryGetValue(c.Arg<int>(), out var l) ? l.ToList() : new List<Model.Risks.Chain.RiskChainLinkDto>();
            return Task.FromResult(new Model.Risks.Chain.RiskChainDto
            {
                RiskId = c.Arg<int>(),
                Levels = new List<Model.Risks.Chain.RiskChainLevelDto> { new() { Links = links } }
            });
        });
        RisksService.GetRiskMgmtReviews(Arg.Any<int>()).Returns(c => new List<MgmtReview>
        {
            new() { Id = 1, RiskId = c.Arg<int>(), Comments = c.Arg<int>() == 1 ? "Reviewed" : Marker + " review" }
        });
        Mitigations.GetByRiskIdAsync(Arg.Any<int>()).Returns(c => Task.FromResult<Mitigation?>(new Mitigation
        {
            Id = c.Arg<int>(), RiskId = c.Arg<int>(), CurrentSolution = c.Arg<int>() == 1 ? "Patch" : Marker + " mitigation"
        }));

        FindingsService.GetFilteredAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(_ => Task.FromResult(Tuple.Create(Findings.ToList(), Findings.Count, true)));
        FindingsService.GetOneAsync(Arg.Any<int>()).Returns(c => Task.FromResult(Findings.First(f => f.Id == c.Arg<int>())));
        FindingsService.GetStatusHistoryAsync(Arg.Any<int>()).Returns(c => Task.FromResult(new List<FindingStatusHistory>
        {
            new() { Id = 1, VulnerabilityId = c.Arg<int>(), ToStatus = FindingStatus.Verified, Justification = c.Arg<int>() == 1 ? "ok" : Marker }
        }));
        FindingsService.UpdateLifecycleStatusAsync(Arg.Any<int>(), Arg.Any<FindingStatus>(), Arg.Any<string?>(), Arg.Any<int?>())
            .Returns(c => Task.FromResult(new Vulnerability { Id = c.ArgAt<int>(0), LifecycleStatus = c.ArgAt<FindingStatus>(1) }));
        FindingsService.AddActionAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<NrAction>()).Returns(c => Task.FromResult(c.ArgAt<NrAction>(2)));
        Teams.GetAllAsync().Returns(_ => Task.FromResult(new List<Team> { new() { Value = 2, Name = "Blue" }, new() { Value = 3, Name = "Red" } }));

        IncidentsService.GetAllAsync().Returns(_ => Task.FromResult(Incidents.ToList()));
        IncidentsService.GetIncidentResponsPlanIdsByIdAsync(Arg.Any<int>()).Returns(_ => Task.FromResult(new List<int>()));
        IncidentsService.UpdateAsync(Arg.Any<Incident>()).Returns(c => Task.FromResult(c.Arg<Incident>()));

        AssessmentsService.GetAssessmentsAsync().Returns(_ => Task.FromResult<List<Assessment>?>(Assessments.ToList()));
        AssessmentsService.GetAssessmentRuns(Arg.Any<int>()).Returns(c => Runs.Where(r => r.AssessmentId == c.Arg<int>()).ToList());
        AssessmentsService.GetAssessmentRunAnsers(Arg.Any<int>(), Arg.Any<int>()).Returns(c => c.ArgAt<int>(1) == 1
            ? new List<AssessmentRunsAnswer>()
            : new List<AssessmentRunsAnswer> { new() { Id = 9, QuestionId = 2, Question = new AssessmentQuestion { Question = Marker } } });
        AssessmentsService.GetAssessmentQuestions(Arg.Any<int>()).Returns(new List<AssessmentQuestion> { new() { Id = 1, AssessmentId = 1, Question = "Backups?" } });
        AssessmentsService.GetAssessmentAnswers(Arg.Any<int>()).Returns(new List<AssessmentAnswer> { new() { Id = 1, AssessmentId = 1, QuestionId = 1, Answer = "Yes" } });
        AssessmentsService.CreateRunAnswer(Arg.Any<int>(), Arg.Any<AssessmentRunsAnswerDto>()).Returns(new AssessmentRunsAnswer { Id = 10 });
        AssessmentsService.When(a => a.UpdateAssessmentRun(Arg.Any<AssessmentRunDto>()))
            .Do(c => Runs.First(r => r.Id == c.Arg<AssessmentRunDto>().Id).Comments = c.Arg<AssessmentRunDto>().Comments);
        Evidence.GetRunEvidenceAsync(Arg.Any<int>()).Returns(c => Task.FromResult(new List<AssessmentAnswerEvidence>
        {
            new() { QuestionId = 1, Name = c.Arg<int>() == 1 ? "portal.pdf" : Marker + ".pdf", Type = "application/pdf" }
        }));

        EntitiesService.GetAllFromServerAsync(Arg.Any<bool>()).Returns(_ => Task.FromResult(Entities.ToList()));
        EntitiesService.SaveEntityAsync(Arg.Any<EntityDto>()).Returns(c => Task.FromResult<Entity?>(Entities.First(e => e.Id == c.Arg<EntityDto>().Id)));

        HostsService.GetFilteredAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(c => (HostsFor(c.ArgAt<string?>(2)), Hosts.Count));
        HostsService.GetAllHostServiceAsync(Arg.Any<int>()).Returns(_ => Task.FromResult(new List<HostsService>()));
        HostsService.GetVulnerabilitySummaryAsync(Arg.Any<int>()).Returns(_ => Task.FromResult(new HostVulnerabilitySummaryDto()));

        RiskIndex.GetOverviewAsync(Arg.Any<string?>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
            .Returns(c => Task.FromResult(Overview(c.ArgAt<string?>(0))));
        RiskIndex.GetNodesAsync(Arg.Any<string?>(), Arg.Any<RiskIndexDimension>(), Arg.Any<string?>(), Arg.Any<DateOnly?>(),
                Arg.Any<CancellationToken>())
            .Returns(c => Task.FromResult(new RiskIndexNodeChildrenDto(Node(c.ArgAt<string?>(0)), RiskIndexDimension.Unit,
                Array.Empty<RiskIndexComparabilityGroupDto>(),
                new[] { Row("e:22", "Portal"), Row("e:20", Marker + " clinic"), Row("u", "Unassigned") }, 0)));

        Catalogue.GetRecordsAsync(Arg.Any<bool>()).Returns(_ =>
        {
            Interlocked.Increment(ref ClassificationReads);
            return Task.FromResult(Records.ToList());
        });
        Flags.GetFlaggedAsync(Arg.Any<RiskFlagCode?>(), Arg.Any<bool?>()).Returns(_ =>
        {
            Interlocked.Increment(ref ClassificationReads);
            return Task.FromResult(new List<FlaggedRiskDto>
            {
                new() { RiskId = 3, Subject = Marker, Flags = new List<RiskFlagCode> { RiskFlagCode.SensitiveData } }
            });
        });
    }

    private List<Host> HostsFor(string? filter)
    {
        if (filter is null || !filter.StartsWith("id==", StringComparison.Ordinal)) return Hosts.ToList();
        var ids = filter["id==".Length..].Split('|').Select(int.Parse).ToHashSet();
        return Hosts.Where(h => ids.Contains(h.Id)).ToList();
    }

    private static RiskIndexNodeRef Node(string? key) =>
        new(key ?? "org", key == "e:20" ? Marker + " clinic" : key == "e:22" ? "Portal" : "Organization", key is null ? "org" : "entity");

    private static RiskIndexNodeRowDto Row(string key, string label) => new(new RiskIndexNodeRef(key, label, "entity"), 40, "high",
        RiskIndexDisplayState.Point, RiskIndexProvenance.Official, 1, 0.9, 3, 0, false, "g", null, null, null);

    private static RiskIndexOverviewDto Overview(string? key) => new(Node(key),
        new[] { new RiskIndexNodeRef("org", "Organization", "org"), new RiskIndexNodeRef("e:20", Marker + " clinic", "entity"), Node(key) },
        new DateOnly(2026, 11, 1), new DateTime(2026, 11, 1), RiskIndexProvenance.Official, null!, RiskIndexMode.Full, null,
        Array.Empty<RiskIndexCategoryDto>(), Array.Empty<RiskIndexSealDto>(), Array.Empty<RiskIndexChipDto>(), null!, 1,
        Array.Empty<RiskIndexBandDto>(), Array.Empty<RiskIndexNodeRef>());

    /// <summary>The production source over this world's services; <see cref="FailEntities"/> breaks its entity read.</summary>
    public IAgentClassificationSource Source() => new WorldSource(this);

    public AgentClassificationOptions Classification() => new(Source(), () => Policy);

    public IReadOnlyList<IAgentTool> Tools() =>
        AgentReadToolCatalog.Create(
                new AgentReadServices(RisksService, Mitigations, FindingsService, IncidentsService, Plans, AssessmentsService,
                    Evidence, EntitiesService, HostsService),
                new RiskOverviewAgentTools(RiskIndex).Tools)
            .Concat(AgentEditToolCatalog.Create(new AgentEditServices(RisksService, FindingsService, Teams, IncidentsService,
                AssessmentsService, EntitiesService, HostsService)))
            .ToList();

    public AgentReadRig Rig() => AgentReadRig.OverTools(Tools(), Classification(), AgentReadRig.Everything());

    public JsonObject EditArguments(string tool, bool excluded)
    {
        var risk = Risks.First(r => r.Id == (excluded ? 2 : 1));
        var finding = Findings.First(f => f.Id == (excluded ? 2 : 1));
        var incident = Incidents.First(i => i.Id == (excluded ? 2 : 1));
        var run = Runs.First(r => r.Id == (excluded ? 2 : 1));
        var entity = Entities.First(e => e.Id == (excluded ? 20 : 22));
        var host = Hosts.First(h => h.Id == (excluded ? 2 : 1));
        return tool switch
        {
            "update_risk" => new JsonObject { ["risk_id"] = risk.Id, ["version"] = AgentVersions.Risk(risk), ["notes"] = "New notes" },
            "update_finding_status" => new JsonObject { ["finding_id"] = finding.Id, ["version"] = AgentVersions.Finding(finding), ["status"] = "verified" },
            "update_finding_assignment" => new JsonObject { ["finding_id"] = finding.Id, ["version"] = AgentVersions.Finding(finding), ["fix_team_id"] = 3 },
            "add_finding_note" => new JsonObject { ["finding_id"] = finding.Id, ["note"] = "Checked" },
            "update_incident" => new JsonObject { ["incident_id"] = incident.Id, ["version"] = AgentVersions.Incident(incident), ["notes"] = "More" },
            "answer_assessment_question" => new JsonObject { ["assessment_id"] = 1, ["run_id"] = run.Id, ["question_id"] = 1, ["answer_id"] = 1 },
            "comment_assessment_run" => new JsonObject { ["assessment_id"] = 1, ["run_id"] = run.Id, ["version"] = AgentVersions.AssessmentRun(run), ["comments"] = "Reviewed" },
            "update_entity_fields" => new JsonObject { ["entity_id"] = entity.Id, ["version"] = AgentVersions.Entity(entity), ["description"] = "New" },
            "update_host_fields" => new JsonObject { ["host_id"] = host.Id, ["version"] = AgentVersions.Host(host), ["comment"] = "New" },
            _ => throw new Xunit.Sdk.XunitException("No arguments for " + tool)
        };
    }

    /// <summary>How many write calls reached ClientServices.</summary>
    public int Writes() =>
        RisksService.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IRisksService.SaveRisk)) +
        FindingsService.ReceivedCalls().Count(c => c.GetMethodInfo().Name is nameof(IVulnerabilitiesService.UpdateLifecycleStatusAsync)
            or nameof(IVulnerabilitiesService.UpdateAsync) or nameof(IVulnerabilitiesService.AddActionAsync)) +
        IncidentsService.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IIncidentsService.UpdateAsync)) +
        AssessmentsService.ReceivedCalls().Count(c => c.GetMethodInfo().Name is nameof(IAssessmentsService.CreateRunAnswer)
            or nameof(IAssessmentsService.UpdateAssessmentRun)) +
        EntitiesService.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IEntitiesService.SaveEntityAsync)) +
        HostsService.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IHostsService.UpdateHostAsync));

    public void AssertNothingWritten() => Assert.Equal(0, Writes());

    private sealed class WorldSource(ClassifiedWorld world) : IAgentClassificationSource
    {
        private readonly ClientServicesClassificationSource _production = new(world.EntitiesService, world.Catalogue,
            world.Flags, world.HostsService, world.AssessmentsService, world.Chains);

        public Task<AgentRiskChainTargets> RiskChainAsync(int riskId, CancellationToken cancellationToken) =>
            _production.RiskChainAsync(riskId, cancellationToken);

        public Task<IReadOnlyList<Entity>> EntitiesAsync(CancellationToken cancellationToken) =>
            world.FailEntities ? throw new HttpRequestException("unreachable") : _production.EntitiesAsync(cancellationToken);

        public Task<IReadOnlyList<DataRecordSummaryDto>> DataRecordsAsync(CancellationToken cancellationToken) =>
            _production.DataRecordsAsync(cancellationToken);

        public Task<IReadOnlyCollection<int>> SensitiveDataRiskIdsAsync(CancellationToken cancellationToken) =>
            _production.SensitiveDataRiskIdsAsync(cancellationToken);

        public Task<IReadOnlyList<Host>> HostsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken) =>
            _production.HostsAsync(ids, cancellationToken);

        public Task<IReadOnlyList<Assessment>> AssessmentsAsync(CancellationToken cancellationToken) =>
            _production.AssessmentsAsync(cancellationToken);
    }
}
