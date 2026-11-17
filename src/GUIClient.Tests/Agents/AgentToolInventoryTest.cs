using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Agents;
using GUIClient.Agents.ReadTools;
using GUIClient.Tools.RiskIndex;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace GUIClient.Tests.Agents;

/// <summary>
/// T353 (S55 "Never exposed"): the tool surface is the reviewed list in <see cref="AgentToolInventory"/>, which the registry
/// enforces — a tool not on it, or differing from its review, is refused wherever it was composed (M62 review finding 4).
/// Names saying delete, approve, sign, permission, secret, credential, key, token, setting or backup fail, and so does
/// any edit tool in M62. The GUI's composition is the DI factory tested here.
/// </summary>
[TestSubject(typeof(AgentToolInventory))]
public class AgentToolInventoryTest
{
    private static IReadOnlyList<IAgentTool> Catalog() => new AgentReadFixtures().Catalog();

    [Fact]
    public void TestTheCatalogIsExactlyTheReviewedSurfaceWithItsReviewedRules()
    {
        var catalog = Catalog();

        Assert.Equal(AgentToolInventory.Reviewed.Keys.OrderBy(n => n), catalog.Select(t => t.Name).OrderBy(n => n));
        foreach (var tool in catalog)
        {
            var review = AgentToolInventory.Reviewed[tool.Name];
            Assert.True(review.Permission == tool.Permission.Description,
                $"{tool.Name} is gated on '{tool.Permission.Description}', reviewed as '{review.Permission}'.");
            Assert.Equal(review.RequiresEdit, tool.RequiresEdit);
        }
    }

    [Fact]
    public void TestTheDiFactoryTheGuiUsesComposesExactlyTheReviewedSurface()
    {
        // The same registrations GeneralServicesBootstrapper makes, over substitutes.
        var services = new ServiceCollection();
        foreach (var contract in new[]
                 {
                     typeof(IRisksService), typeof(IMitigationService), typeof(IVulnerabilitiesService), typeof(IIncidentsService),
                     typeof(IIncidentResponsePlansService), typeof(IAssessmentsService), typeof(IAssessmentEvidenceService),
                     typeof(IEntitiesService), typeof(IHostsService), typeof(IRiskIndexService)
                 })
            services.AddSingleton(contract, Substitute.For(new[] { contract }, Array.Empty<object>()));
        services.AddSingleton<IAgentToolSource>(sp => new RiskOverviewAgentTools(sp.GetRequiredService<IRiskIndexService>()));

        var tools = AgentReadToolCatalog.Create(services.BuildServiceProvider());
        var registry = new AgentToolRegistry(tools, McpDispatcher.BootstrapToolNames);

        Assert.Equal(AgentToolInventory.Reviewed.Keys.OrderBy(n => n), registry.Tools.Select(t => t.Name));
    }

    [Fact]
    public void TestTheRegistryRefusesAToolThatIsNotOnTheReviewedList()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            new AgentToolRegistry(new IAgentTool[] { new StubTool("read_item", false) }, McpDispatcher.BootstrapToolNames));
        Assert.Contains("not on the reviewed tool list", error.Message);
    }

    [Fact]
    public void TestTheRegistryRefusesAToolThatDiffersFromItsReview()
    {
        // A reviewed name with a looser rule, and one that turned into an edit tool.
        var looser = new StubTool("search_risks", false, AgentToolPermission.SignedIn);
        var editing = new StubTool("get_risk", true, AgentToolPermission.RoleOrPermission("Administrator", "riskmanagement"));

        Assert.Throws<ArgumentException>(() => new AgentToolRegistry(new IAgentTool[] { looser }, McpDispatcher.BootstrapToolNames));
        Assert.Throws<ArgumentException>(() => new AgentToolRegistry(new IAgentTool[] { editing }, McpDispatcher.BootstrapToolNames));
    }

    [Fact]
    public void TestTheRuntimeRefusesToStartWithAnUnreviewedTool()
    {
        Assert.Throws<ArgumentException>(() => new AgentCoworkRuntime(new InMemoryAgentRegistrationStore(), new ScriptedPrompter(),
            new RecordingAuditSink(), new ManualTimeProvider(),
            new AgentCoworkOptions { Tools = Catalog().Append(new StubTool("export_everything", false)).ToList() }));
    }

    [Fact]
    public void TestM62HasNoEditTool()
    {
        Assert.Empty(Catalog().Where(t => t.RequiresEdit).Select(t => t.Name));
        Assert.Empty(AgentToolInventory.Reviewed.Values.Where(t => t.RequiresEdit).Select(t => t.Name));
    }

    [Fact]
    public async Task TestNoListedToolNamesAForbiddenAction()
    {
        // Everything an administrator holding every permission is shown: the bootstrap tools and the whole catalog.
        var admin = AgentReadRig.Everything();
        admin.IsAdmin = true;
        var listed = await AgentReadRig.OverFixtures(user: admin).ListAsync();

        Assert.Equal(McpDispatcher.BootstrapToolNames.Concat(AgentToolInventory.Reviewed.Keys).OrderBy(n => n, StringComparer.Ordinal),
            listed.OrderBy(n => n, StringComparer.Ordinal));
        Assert.DoesNotContain(listed, NamesForbiddenAction);
        Assert.DoesNotContain(AgentToolInventory.Reviewed.Keys, NamesForbiddenAction);
    }

    [Theory]
    [InlineData("delete_risk")]
    [InlineData("bulk_delete_findings")]
    [InlineData("approve_risk_acceptance")]
    [InlineData("sign_off_review")]
    [InlineData("signoff_gate")]
    [InlineData("set_user_permissions")]
    [InlineData("assign_role_permission")]
    [InlineData("read_secret")]
    [InlineData("list_credentials")]
    [InlineData("get_api_key")]
    [InlineData("refresh_token")]
    [InlineData("update_settings")]
    [InlineData("run_backup")]
    [InlineData("restore_backup")]
    [InlineData("reset_password")]
    [InlineData("list_vault_connections")]
    public void TestTheForbiddenNameRuleCatchesEachForbiddenAction(string name)
    {
        Assert.True(NamesForbiddenAction(name), name);
    }

    [Theory]
    [InlineData("search_risks")]
    [InlineData("update_finding_assignment")] // M63: "assignment" is not "sign"
    [InlineData("get_risk_overview")]
    [InlineData("list_risk_reviews")]
    public void TestTheForbiddenNameRuleLeavesOrdinaryNamesAlone(string name)
    {
        Assert.False(NamesForbiddenAction(name), name);
    }

    [Fact]
    public void TestEveryToolDeclaresAnObjectSchemaAndAResultShape()
    {
        foreach (var tool in Catalog())
        {
            Assert.Matches(AgentToolRegistry.NamePattern, tool.Name);
            Assert.Equal("object", (string?)tool.InputSchema()["type"]);
            Assert.False(tool.InputSchema()["properties"]!.AsObject().ContainsKey("agent_key"),
                tool.Name + " must not declare agent_key itself: the dispatcher adds it and never forwards it");
            Assert.NotNull(tool.ResultType);
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
        }
    }

    [Fact]
    public void TestEveryToolHasTestArguments()
    {
        Assert.Equal(AgentToolInventory.Reviewed.Keys.OrderBy(n => n), AgentReadToolArguments.Names.OrderBy(n => n));
    }

    [Fact]
    public void TestTheRegistryRefusesMalformedAndReservedNames()
    {
        var reviewed = AgentRig.ReviewedWithStandIns(new IAgentTool[]
        {
            new StubTool("Get-Risk", false), new StubTool("request_access", false), new StubTool("read_item", false)
        });
        Assert.Throws<ArgumentException>(() => new AgentToolRegistry(new[] { new StubTool("Get-Risk", false) },
            McpDispatcher.BootstrapToolNames, reviewed));
        Assert.Throws<ArgumentException>(() => new AgentToolRegistry(new[] { new StubTool("request_access", false) },
            McpDispatcher.BootstrapToolNames, reviewed));
        Assert.Throws<ArgumentException>(() => new AgentToolRegistry(
            new[] { new StubTool("read_item", false), new StubTool("read_item", false) }, McpDispatcher.BootstrapToolNames, reviewed));
    }

    // Whole words of the name that start with a forbidden stem ("sign" but not "assignment"), and stems that are
    // forbidden anywhere in the name ("apitoken", "bulkdelete").
    private static readonly string[] WordStems =
        { "delete", "remove", "purge", "approv", "sign", "permission", "role", "secret", "credential", "key", "token",
          "setting", "backup", "restore", "password", "vault" };

    private static readonly string[] AnywhereStems =
        { "delete", "approv", "permission", "secret", "credential", "token", "setting", "backup", "password", "vault" };

    private static bool NamesForbiddenAction(string name) =>
        name.Split('_').Any(word => WordStems.Any(stem => word.StartsWith(stem, StringComparison.Ordinal)))
        || AnywhereStems.Any(stem => name.Contains(stem, StringComparison.Ordinal));
}
