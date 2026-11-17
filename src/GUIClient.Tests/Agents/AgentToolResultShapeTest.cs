using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using GUIClient.Agents;
using GUIClient.Agents.ReadTools;
using JetBrains.Annotations;
using Model.Exceptions;
using Xunit;

namespace GUIClient.Tests.Agents;

/// <summary>
/// T352 (S55) and the M62 review: every result is shaped by an allowlist type the registry checks and the dispatcher
/// enforces, travels inside the untrusted-data envelope, carries no vault reference, credential or personal contact
/// field, and is bounded — long text clipped, pages capped, nested lists paged, the whole result under a size cap.
/// </summary>
[TestSubject(typeof(AgentResultShape))]
public class AgentToolResultShapeTest
{
    public static TheoryData<string> Tools() => AgentReadToolArguments.AllTools();

    // ---- The allowlist types (finding 8) -------------------------------------------------------------------------

    [Fact]
    public void TestNoResultShapeNamesACredentialOrCarriesARawType()
    {
        var problems = new AgentReadFixtures().Catalog()
            .SelectMany(t => AgentResultShape.Problems(t.ResultType).Select(p => t.Name + ": " + p)).ToList();

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void TestEveryCatalogToolSerializesThroughItsDeclaredType()
    {
        foreach (var tool in new AgentReadFixtures().Catalog())
        {
            var type = tool.GetType();
            Assert.True(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(AgentReadTool<>), tool.Name);
            Assert.Equal(tool.ResultType, type.GetGenericArguments()[0]);
        }
    }

    // The guard itself, on shapes it must refuse: without these it could pass by scanning nothing.
    private sealed record LeakyKey(int Id, string ApiKey);
    private sealed record LeakyNested(int Id, IReadOnlyList<LeakyInner> Items);
    private sealed record LeakyInner(string Name, string? PasswordHash);
    private sealed record RawEntity(int Id, DAL.Entities.Risk Risk);
    private sealed record RawObject(int Id, object Payload);
    private sealed record RawBytes(int Id, byte[] Content);
    private sealed record RawMap(int Id, Dictionary<string, string> Values);
    private sealed record VaultReference(int Id, string? VaultRef);
    private sealed record TokenField(int Id, string AccessToken);
    private sealed class NotARecord { public int Id { get; init; } }

    [Theory]
    [InlineData(typeof(LeakyKey))]
    [InlineData(typeof(LeakyNested))]
    [InlineData(typeof(RawEntity))]
    [InlineData(typeof(RawObject))]
    [InlineData(typeof(RawBytes))]
    [InlineData(typeof(RawMap))]
    [InlineData(typeof(VaultReference))]
    [InlineData(typeof(TokenField))]
    [InlineData(typeof(NotARecord))]
    [InlineData(typeof(AgentPage<LeakyInner>))]
    public void TestTheShapeGuardRefusesALeakyShape(Type shape)
    {
        Assert.NotEmpty(AgentResultShape.Problems(shape));
    }

    /// <summary>A tool that is not an AgentReadTool, declaring a clean shape and returning more than it declares.</summary>
    private sealed class RogueTool : IAgentTool
    {
        public string Name => "read_item";
        public string Description => "rogue";
        public bool RequiresEdit => false;
        public AgentToolPermission Permission { get; } = AgentToolPermission.AnyOf(StubTool.StubPermission);
        public Type ResultType => typeof(StubResult);
        public JsonObject InputSchema() => new() { ["type"] = "object", ["properties"] = new JsonObject() };

        public Task<JsonObject> InvokeAsync(AgentToolCall call, JsonObject arguments, CancellationToken cancellationToken) =>
            Task.FromResult(new JsonObject
            {
                ["status"] = "ok", ["tool"] = "read_item", ["api_key"] = "AKIA-rogue-literal"
            });
    }

    [Fact]
    public async Task TestAResultOutsideItsDeclaredShapeIsDropped()
    {
        var rogue = new RogueTool();
        var rig = new AgentRig(tools: new IAgentTool[] { rogue });
        var (agent, key) = rig.RegisterApproved();
        rig.Grant(agent, 1, AgentAccessMode.Read);

        var response = await rig.SendAsync(AgentRig.Post(AgentRig.ToolCall(4, "read_item", $"{{\"agent_key\":\"{key}\"}}")));

        Assert.DoesNotContain("AKIA-rogue-literal", response.Body);
        Assert.Equal(AgentToolErrors.ToolFailed, (string?)JsonNode.Parse(response.Body)!["result"]!["structuredContent"]!["status"]);
        Assert.Contains(rig.Audit.Events, e => e.Kind == AgentAuditKind.ToolFailed && e.Detail.Contains("api_key"));
    }

    [Fact]
    public void TestTheRegistryRefusesAToolDeclaringALeakyShape()
    {
        var leaky = new LeakyShapeTool();
        Assert.Throws<ArgumentException>(() => new AgentToolRegistry(new IAgentTool[] { leaky },
            McpDispatcher.BootstrapToolNames, AgentRig.ReviewedWithStandIns(null).Append(
                    new KeyValuePair<string, ReviewedAgentTool>("read_item",
                        new ReviewedAgentTool("read_item", leaky.Permission.Description, false)))
                .ToDictionary(p => p.Key, p => p.Value)));
    }

    private sealed class LeakyShapeTool : IAgentTool
    {
        public string Name => "read_item";
        public string Description => "leaky";
        public bool RequiresEdit => false;
        public AgentToolPermission Permission { get; } = AgentToolPermission.AnyOf(StubTool.StubPermission);
        public Type ResultType => typeof(LeakyKey);
        public JsonObject InputSchema() => new() { ["type"] = "object" };
        public Task<JsonObject> InvokeAsync(AgentToolCall call, JsonObject arguments, CancellationToken cancellationToken) =>
            Task.FromResult(new JsonObject());
    }

    // ---- Credential names and values (finding 2) -----------------------------------------------------------------

    [Theory]
    [InlineData("ApiKey")]
    [InlineData("api_key")]
    [InlineData("encryptedApiKey")]
    [InlineData("KeyHash")]
    [InlineData("password")]
    [InlineData("adminPassword")]
    [InlineData("clientSecret")]
    [InlineData("refresh_token")]
    [InlineData("vaultReference")]
    [InlineData("Salt")]
    // The bypasses the review listed: Portuguese, other credential words, joined and all-caps forms.
    [InlineData("senha")]
    [InlineData("senhaAdmin")]
    [InlineData("chave")]
    [InlineData("chaveApi")]
    [InlineData("chave_privada")]
    [InlineData("segredo")]
    [InlineData("passphrase")]
    [InlineData("passcode")]
    [InlineData("community")]
    [InlineData("snmpCommunity")]
    [InlineData("connectionString")]
    [InlineData("CONNECTION_STRING")]
    [InlineData("dsn")]
    [InlineData("db-dsn")]
    [InlineData("otp")]
    [InlineData("otpSeed")]
    [InlineData("SSHKEY")]
    [InlineData("KEYFILE")]
    [InlineData("privkey")]
    [InlineData("db_pass")]
    public void TestCredentialLikeNamesAreCaught(string name)
    {
        Assert.True(AgentSecretNames.IsCredentialLike(name), name);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("monkeyCount")]
    [InlineData("NodeRef")]
    [InlineData("QualityBand")]
    [InlineData("description")]
    [InlineData("criticality")]
    [InlineData("technicalOwner")]
    public void TestOrdinaryNamesAreNotCredentialLike(string name)
    {
        Assert.False(AgentSecretNames.IsCredentialLike(name), name);
    }

    [Theory]
    [InlineData("vault:v1:3:c2VjcmV0")]
    [InlineData("see vault:v2:1:aWQ:Zg:b3B0 for it")]
    [InlineData("-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBg")]
    [InlineData("-----BEGIN OPENSSH PRIVATE KEY-----")]
    [InlineData("Authorization: Bearer abc.def.ghi")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0In0.sig")]
    [InlineData("password=hunter2")]
    [InlineData("Senha: segredo123")]
    [InlineData("api_key = AKIA123")]
    [InlineData("wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY")]
    [InlineData("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")]
    public void TestSecretLookingValuesAreCaught(string value)
    {
        Assert.True(AgentDataPolicy.LooksLikeSecretValue(value), value);
    }

    [Theory]
    [InlineData("Runs the monthly payroll")]
    [InlineData("4")]
    [InlineData("https://intranet.example.test/payroll")]
    [InlineData("2026-11-17")]
    [InlineData("Ação de resposta a incidentes")]
    public void TestOrdinaryValuesAreNotSecretLooking(string value)
    {
        Assert.False(AgentDataPolicy.LooksLikeSecretValue(value), value);
    }

    [Fact]
    public async Task TestOnlyReviewedSafeEntityPropertiesAreReturnedAndTheRestCounted()
    {
        var answer = await AgentReadRig.OverFixtures().CallAsync("get_entity");

        var names = answer.Data!["properties"]!["items"]!.AsArray().Select(p => (string)p!["name"]!).ToList();
        Assert.Equal(new[] { "name", "description", "criticality" }, names);
        Assert.Equal(5, (int)answer.Data["withheld_properties"]!);
        Assert.Equal(3, (int)answer.Structured["withheld_secret_fields"]!);     // apiKey, adminPassword, a vault value
        Assert.Equal(1, (int)answer.Structured["withheld_personal_fields"]!);   // email
        Assert.Equal(1, (int)answer.Structured["withheld_undeclared_fields"]!); // customerField
        Assert.DoesNotContain("undeclared-literal", answer.Body);
    }

    // ---- Personal data (finding 9) -------------------------------------------------------------------------------

    [Fact]
    public async Task TestPersonalContactDataIsWithheldButNamesAreKept()
    {
        Assert.True(AgentDataPolicy.WithholdPersonalContactData);
        var rig = AgentReadRig.OverFixtures();

        var person = await rig.CallAsync("get_entity", new JsonObject { ["entity_id"] = 3 });
        var search = await rig.CallAsync("search_entities");

        Assert.Equal("Child", (string?)person.Data!["name"]);
        Assert.DoesNotContain(person.Data["properties"]!["items"]!.AsArray(), p => (string)p!["name"]! == "phoneNumber");
        Assert.Equal(1, (int)person.Structured["withheld_personal_fields"]!);
        Assert.DoesNotContain(AgentReadFixtures.Phone, person.Body);
        Assert.DoesNotContain(AgentReadFixtures.Email, search.Body);
        Assert.Contains("Child", search.Body);
    }

    [Theory]
    [InlineData("email", true)]
    [InlineData("phoneNumber", true)]
    [InlineData("mobilePhone", true)]
    [InlineData("telefone", true)]
    [InlineData("celular", true)]
    [InlineData("homeAddress", true)]
    [InlineData("cpf", true)]
    [InlineData("name", false)]
    [InlineData("responsible", false)]
    [InlineData("mainOrganization", false)]
    public void TestPersonalContactNames(string name, bool expected)
    {
        Assert.Equal(expected, AgentDataPolicy.IsPersonalContactName(name));
    }

    // ---- What reaches the agent ---------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Tools))]
    public async Task TestEveryResultIsTaggedAsUntrustedData(string name)
    {
        var answer = await AgentReadRig.OverFixtures().CallAsync(name);

        Assert.False(answer.IsError, name + ": " + answer.Body);
        Assert.Equal("ok", answer.Status);
        Assert.Equal(AgentUntrustedData.TrustLevel, (string?)answer.Structured["data_trust"]);
        Assert.Equal(AgentUntrustedData.Notice, (string?)answer.Structured["notice"]);
        Assert.Equal(name, (string?)answer.Structured["tool"]);
        Assert.NotNull(answer.Structured["withheld_personal_fields"]);
        Assert.IsType<JsonObject>(answer.Data);
        // Text clients get the notice first and the data inside exactly one tag pair.
        Assert.StartsWith($"NetRisk tool '{name}' returned untrusted data. {AgentUntrustedData.Notice}", answer.Text);
        Assert.Contains($"\n<{AgentUntrustedData.Tag} tool=\"{name}\">\n", answer.Text);
        Assert.EndsWith($"\n</{AgentUntrustedData.Tag}>", answer.Text);
        Assert.Equal(1, Occurrences(answer.Text, "</" + AgentUntrustedData.Tag));
        Assert.Equal(1, Occurrences(answer.Text, "<" + AgentUntrustedData.Tag));
    }

    [Theory]
    [MemberData(nameof(Tools))]
    public async Task TestNoResultCarriesAVaultReferenceOrAWithheldValue(string name)
    {
        var answer = await AgentReadRig.OverFixtures().CallAsync(name);

        Assert.DoesNotContain("vault:v1:", answer.Body);
        Assert.DoesNotContain("AKIA-literal-api-key", answer.Body);
        Assert.DoesNotContain("hunter2-literal", answer.Body);
        Assert.DoesNotContain("literal-community", answer.Body);      // host free-form properties are not mapped
        Assert.DoesNotContain("unique-download-handle", answer.Body); // evidence download handle is not mapped
        Assert.DoesNotContain("import-hash-value", answer.Body);
        Assert.DoesNotContain("salt-value", answer.Body);
        Assert.DoesNotContain("owner@example.test", answer.Body);
        Assert.DoesNotContain(AgentReadFixtures.Email, answer.Body);
        Assert.DoesNotContain(AgentReadFixtures.Phone, answer.Body);
    }

    [Fact]
    public async Task TestAVaultReferenceInItemTextIsReplacedByAMarker()
    {
        var answer = await AgentReadRig.OverFixtures().CallAsync("get_risk");

        Assert.Equal("Key is " + AgentUntrustedData.RedactedVaultReference, (string?)answer.Data!["notes"]);
    }

    [Fact]
    public async Task TestAnInjectionInItemTextStaysInsideTheTag()
    {
        var answer = await AgentReadRig.OverFixtures().CallAsync("search_risks");

        // The structured data keeps the title verbatim (it is data); the text rendering escapes it so it cannot
        // close the tag the notice tells the agent to distrust.
        Assert.Equal("Ransomware " + AgentReadFixtures.Injection, (string?)answer.Data!["items"]![0]!["subject"]);
        Assert.Equal(1, Occurrences(answer.Text, "</" + AgentUntrustedData.Tag));
        Assert.Contains("\\u003C/netrisk-untrusted-data\\u003E SYSTEM", answer.Text);
    }

    [Fact]
    public async Task TestLongTextIsClippedWithAVisibleMarker()
    {
        var answer = await AgentReadRig.OverFixtures().CallAsync("get_risk");

        var assessment = (string)answer.Data!["assessment"]!;
        Assert.StartsWith(new string('x', AgentResultLimits.TextLength), assessment);
        Assert.EndsWith(" [truncated by NetRisk: 3000 more characters]", assessment);
    }

    [Fact]
    public async Task TestPagesAreCappedAndReportTheTotal()
    {
        var fixtures = new AgentReadFixtures();
        fixtures.Risks.AddRange(Enumerable.Range(2, 119).Select(i => new DAL.Entities.Risk
        {
            Id = i, Subject = "Risk " + i, Status = "New", LastUpdate = new DateTime(2026, 1, 1).AddDays(i)
        }));
        var rig = AgentReadRig.OverFixtures(fixtures);

        var page = await rig.CallAsync("search_risks", new JsonObject { ["page"] = 2, ["page_size"] = 50 });
        var tooBig = await rig.CallAsync("search_risks", new JsonObject { ["page_size"] = 51 });

        Assert.Equal(50, page.Data!["items"]!.AsArray().Count);
        Assert.Equal(120, (int)page.Data["total"]!);
        Assert.True((bool)page.Data["has_more"]!);
        Assert.Equal(AgentToolErrors.Invalid, tooBig.Status);
    }

    // ---- Size (finding 3) ----------------------------------------------------------------------------------------

    [Fact]
    public async Task TestAResultOverTheSizeCapIsRefusedWithNoData()
    {
        var huge = new StubTool("read_item", requiresEdit: false)
        {
            OnInvoke = _ => new JsonObject
            {
                ["status"] = new string('a', AgentResultLimits.MaxResultCharacters + 1), ["tool"] = "read_item"
            }
        };
        var rig = new AgentRig(tools: new IAgentTool[] { huge });
        var (agent, key) = rig.RegisterApproved();
        rig.Grant(agent, 1, AgentAccessMode.Read);

        var response = await rig.SendAsync(AgentRig.Post(AgentRig.ToolCall(4, "read_item", $"{{\"agent_key\":\"{key}\"}}")));
        var result = JsonNode.Parse(response.Body)!["result"]!;

        Assert.True((bool)result["isError"]!);
        Assert.Equal("result_too_large", (string?)result["structuredContent"]!["status"]);
        Assert.True(response.Body.Length < 10_000, "a refused result must not carry the data");
    }

    [Theory]
    [MemberData(nameof(Tools))]
    public async Task TestEveryToolAnswersItsWorstCaseWithinTheSizeCap(string name)
    {
        var rig = AgentReadRig.OverTools(AgentReadWorstCase.Catalog());

        var answer = await rig.CallAsync(name);

        Assert.False(answer.IsError, $"{name}: {answer.Status}");
        Assert.True(answer.Text.Length <= AgentResultLimits.MaxResultCharacters, $"{name}: {answer.Text.Length} characters");
    }

    [Fact]
    public async Task TestALargeResponsePlanIsReadInPages()
    {
        var rig = AgentReadRig.OverTools(AgentReadWorstCase.Catalog());

        var first = await rig.CallAsync("get_response_plan", new JsonObject { ["plan_id"] = 1 });
        var last = await rig.CallAsync("get_response_plan",
            new JsonObject { ["plan_id"] = 1, ["task_page"] = 8, ["execution_page"] = 8 });
        var smaller = await rig.CallAsync("get_response_plan", new JsonObject { ["plan_id"] = 1, ["items_per_page"] = 5 });

        Assert.Equal(AgentResultLimits.NestedPageSize, first.Data!["tasks"]!["items"]!.AsArray().Count);
        Assert.Equal(AgentReadWorstCase.Items, (int)first.Data["tasks"]!["total"]!);
        Assert.True((bool)first.Data["tasks"]!["has_more"]!);
        Assert.Equal(AgentReadWorstCase.Items - 7 * AgentResultLimits.NestedPageSize, last.Data!["tasks"]!["items"]!.AsArray().Count);
        Assert.False((bool)last.Data["executions"]!["has_more"]!);
        Assert.Equal(141, (int)last.Data["tasks"]!["items"]![0]!["id"]!);
        Assert.Equal(5, smaller.Data!["executions"]!["items"]!.AsArray().Count);
    }

    // ---- Other contracts -----------------------------------------------------------------------------------------

    [Fact]
    public async Task TestWhoAmIReturnsTheUserNameAndRoleNamesOnly()
    {
        var user = AgentReadRig.Everything();
        user.IsAdmin = true;
        var answer = await AgentReadRig.OverFixtures(user: user).CallAsync("whoami");

        Assert.Equal(new[] { "roles", "user_name" }, answer.Data!.AsObject().Select(p => p.Key).OrderBy(k => k));
        Assert.Equal("Ana Analyst", (string?)answer.Data["user_name"]);
        Assert.Equal(new[] { "Analyst", "Admin" }, answer.Data["roles"]!.AsArray().Select(r => (string)r!));
        Assert.DoesNotContain("ana@example.test", answer.Body);
        Assert.DoesNotContain("riskmanagement", answer.Body);
    }

    [Theory]
    [InlineData("get_risk", "{}", "risk_id")]
    [InlineData("get_risk", "{\"risk_id\":\"one\"}", "risk_id")]
    [InlineData("get_risk", "{\"risk_id\":0}", "risk_id")]
    [InlineData("get_finding", "{\"finding_id\":1.5}", "finding_id")]
    [InlineData("search_incidents", "{\"page\":0}", "page")]
    [InlineData("search_hosts", "{\"query\":7}", "query")]
    [InlineData("list_risk_overview_children", "{\"dimension\":\"Galaxy\"}", "dimension")]
    [InlineData("get_response_plan", "{\"plan_id\":1,\"items_per_page\":21}", "items_per_page")]
    public async Task TestBadArgumentsAreACleanInvalidError(string tool, string arguments, string argument)
    {
        var answer = await AgentReadRig.OverFixtures().CallAsync(tool, JsonNode.Parse(arguments)!.AsObject());

        Assert.True(answer.IsError);
        Assert.Equal(AgentToolErrors.Invalid, answer.Status);
        Assert.Contains("'" + argument + "'", (string?)answer.Structured["message"]);
    }

    [Fact]
    public async Task TestAnOverlongQueryIsRefusedNotCut()
    {
        var query = new string('q', AgentResultLimits.MaxQueryLength + 1);
        var answer = await AgentReadRig.OverFixtures().CallAsync("search_risks", new JsonObject { ["query"] = query });

        Assert.Equal(AgentToolErrors.Invalid, answer.Status);
    }

    [Fact]
    public async Task TestAnItemThatDoesNotExistIsNotFound()
    {
        var answer = await AgentReadRig.OverFixtures().CallAsync("get_risk", new JsonObject { ["risk_id"] = 999 });

        Assert.Equal(AgentToolErrors.NotFound, answer.Status);
        Assert.Null(answer.Data);
    }

    // ---- Error mapping ------------------------------------------------------------------------------------------

    public static TheoryData<Exception, string> Failures() => new()
    {
        { new RestComunicationException("x", new HttpRequestException("vault:v1:1:abc", null, HttpStatusCode.Forbidden)), "refused" },
        { new HttpRequestException("denied", null, HttpStatusCode.Unauthorized), "refused" },
        { new InvalidHttpRequestException("x", "/Risks", "GET") { StatusCode = HttpStatusCode.Forbidden }, "refused" },
        { new RestComunicationException("x", new HttpRequestException("gone", null, HttpStatusCode.NotFound)), "not_found" },
        { new DataNotFoundException("Risk", "1"), "not_found" },
        { new BadFilterException("title@=*x", "server says no"), "invalid_filter" },
        { new HttpRequestException("400", null, HttpStatusCode.BadRequest), "invalid" },
        { new RestComunicationException("x", new HttpRequestException("500", null, HttpStatusCode.InternalServerError)), "server_error" },
        { new RestComunicationException("x", new HttpRequestException("connection refused")), "server_unavailable" },
        { new TaskCanceledException("timeout"), "server_unavailable" },
        { new RestComunicationException("plain"), "server_error" },
        // AssessmentEvidenceRestService: a 403 with the server's reason as the message and no status code.
        { new InvalidHttpRequestException("server says no vault:v1:1:abc", "/x", "GET"), "no_answer" },
        { new InvalidOperationException("secret detail"), "tool_failed" }
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public void TestFailuresMapToAFixedStatusAndMessage(Exception failure, string status)
    {
        var error = AgentToolErrors.From(failure);

        Assert.Equal(status, error.Status);
        Assert.DoesNotContain("vault:", error.Message);
        Assert.DoesNotContain("secret detail", error.Message);
        Assert.DoesNotContain("server says no", error.Message);
    }

    [Fact]
    public void TestRedactionReachesNestedArraysAndKeepsOtherText()
    {
        var data = new JsonObject
        {
            ["a"] = "keep me",
            ["b"] = new JsonArray("x vault:v2:1:aWQ:Zg:b3B0 y", new JsonObject { ["c"] = "vault:v1:9:c2VjcmV0" })
        };

        var wrapped = AgentUntrustedData.Wrap("get_risk", data);

        Assert.Equal("keep me", (string?)wrapped["data"]!["a"]);
        Assert.Equal("x " + AgentUntrustedData.RedactedVaultReference + " y", (string?)wrapped["data"]!["b"]![0]);
        Assert.Equal(AgentUntrustedData.RedactedVaultReference, (string?)wrapped["data"]!["b"]![1]!["c"]);
        Assert.Equal("x vault:v2:1:aWQ:Zg:b3B0 y", (string?)data["b"]![0]); // the tool's own object is not mutated
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var at = text.IndexOf(value, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
