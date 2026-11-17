using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GUIClient.Agents;
using JetBrains.Annotations;
using Tools.Security;
using Xunit;

namespace GUIClient.Tests.Agents;

/// <summary>
/// T347/T349 (S55 "Registration and the agent key"): the key is CSPRNG-length, stored only as its SHA-256, compared in
/// constant time, returned once — and never written to the store, the audit log or any ToString.
/// </summary>
[TestSubject(typeof(AgentKeyIssuer))]
public class AgentKeyHandlingTest
{
    [Fact]
    public void TestKeyCarries256BitsAndNeverRepeats()
    {
        var key = AgentKeyIssuer.NewKey();

        Assert.StartsWith(AgentKeyIssuer.Prefix, key);
        var token = key[AgentKeyIssuer.Prefix.Length..];
        var padded = token.Replace('-', '+').Replace('_', '/') + new string('=', (4 - token.Length % 4) % 4);
        Assert.True(Convert.FromBase64String(padded).Length * 8 >= 256);

        var keys = Enumerable.Range(0, 2000).Select(_ => AgentKeyIssuer.NewKey()).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(2000, keys.Count);
    }

    [Fact]
    public void TestKeysComeFromTheCsprngNeverSystemRandom()
    {
        var keys = AgentSource.Read("AgentKeys.cs");
        Assert.Contains("RandomGenerator.RandomToken(KeyEntropyBytes)", keys);

        foreach (var file in AgentSource.CoreFiles())
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotMatch(new Regex(@"new\s+Random\s*\(|Random\.Shared"), text);
        }
    }

    [Fact]
    public void TestHashIsTheSha256OfTheKeyAndNotTheKey()
    {
        var key = AgentKeyIssuer.NewKey();

        var hash = AgentKeyIssuer.Hash(key);

        Assert.Equal(HashTool.CreateSha256(key), hash);
        Assert.Equal(64, hash.Length);
        Assert.DoesNotContain(key, hash, StringComparison.Ordinal);
    }

    [Fact]
    public void TestOnlyTheIssuedKeyMatchesItsHash()
    {
        var key = AgentKeyIssuer.NewKey();
        var hash = AgentKeyIssuer.Hash(key);

        Assert.True(AgentKeyIssuer.Matches(key, hash));
        Assert.True(AgentKeyIssuer.Matches(key, hash.ToLowerInvariant()));
        Assert.False(AgentKeyIssuer.Matches(AgentKeyIssuer.NewKey(), hash));
        Assert.False(AgentKeyIssuer.Matches(key + "x", hash));
        Assert.False(AgentKeyIssuer.Matches(null, hash));
        Assert.False(AgentKeyIssuer.Matches("", hash));
        Assert.False(AgentKeyIssuer.Matches(new string('a', AgentKeyIssuer.MaxPresentedKeyLength + 1), hash));
        Assert.False(AgentKeyIssuer.Matches(key, "not-a-hash"));
        Assert.False(AgentKeyIssuer.Matches(key, null));
        Assert.False(AgentKeyIssuer.Matches(key, key));
    }

    /// <summary>The comparison is FixedTimeEquals over digests, and no key hash is compared any other way.</summary>
    [Fact]
    public void TestKeyHashesAreComparedInConstantTimeOnly()
    {
        var keys = AgentSource.Read("AgentKeys.cs");
        var matches = keys[keys.IndexOf("public static bool Matches", StringComparison.Ordinal)..];
        matches = matches[..matches.IndexOf("public static string Fingerprint", StringComparison.Ordinal)];
        Assert.Contains("CryptographicOperations.FixedTimeEquals(presented, stored)", matches);

        var registry = AgentSource.Read("AgentRegistry.cs");
        Assert.Contains("AgentKeyIssuer.Matches(presentedKey, registration.KeyHash)", registry);

        foreach (var file in AgentSource.CoreFiles())
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotMatch(new Regex(@"KeyHash\s*[!=]=|==\s*\w+\.KeyHash|KeyHash\.Equals|Equals\([^)]*KeyHash"), text);
        }
    }

    [Fact]
    public async Task TestApprovedKeyIsReturnedOnceAndStoredOnlyAsItsHash()
    {
        var rig = new AgentRig();
        var outcome = rig.Registry.Begin("Claude Code", "Triage", new AgentClientInfo("claude-code", "2.1.0"));
        Assert.True(rig.Registry.Decide(outcome.Pending!.PendingId, approve: true));

        var first = await rig.Registry.WaitAsync(outcome.Pending.PendingId, TimeSpan.Zero, CancellationToken.None);
        var second = await rig.Registry.WaitAsync(outcome.Pending.PendingId, TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(RegistrationResultKind.Approved, first.Kind);
        var key = first.Key!.Reveal();
        Assert.Equal(RegistrationResultKind.NotFound, second.Kind);
        Assert.Null(second.Key);

        var stored = Assert.Single(AgentRegistrationSerializer.Deserialize(rig.Store.Serialized));
        Assert.Equal(AgentKeyIssuer.Hash(key), stored.KeyHash);
        Assert.Equal(AgentRig.UserId, stored.UserId);
        Assert.Equal(first.Registration!.Id, rig.Registry.Authenticate(key)!.Id);
    }

    /// <summary>Regression guard: the full register_agent flow over HTTP leaves the key in exactly one place.</summary>
    [Fact]
    public async Task TestKeyNeverReachesTheStoreTheAuditLogOrAnyToString()
    {
        var rig = new AgentRig();
        var pending = await rig.SendAsync(AgentRig.Post(
            AgentRig.ToolCall(1, "register_agent", "{\"name\":\"Claude Code\",\"description\":\"Triage\"}")));
        var pendingData = StructuredContent(pending);
        Assert.Equal("pending", (string?)pendingData["status"]);
        var pendingId = (string)pendingData["pending_id"]!;
        Assert.Equal(rig.Registry.FindPending(pendingId)!.Fingerprint, (string?)pendingData["fingerprint"]);

        Assert.True(rig.Registry.Decide(pendingId, approve: true));
        var approved = await rig.SendAsync(AgentRig.Post(
            AgentRig.ToolCall(2, "register_agent", $"{{\"pending_id\":\"{pendingId}\"}}")));
        var approvedData = StructuredContent(approved);
        Assert.Equal("approved", (string?)approvedData["status"]);
        var key = (string)approvedData["agent_key"]!;
        Assert.StartsWith(AgentKeyIssuer.Prefix, key);

        Assert.DoesNotContain(key, rig.Store.Serialized!, StringComparison.Ordinal);
        Assert.Contains(AgentKeyIssuer.Hash(key), rig.Store.Serialized!, StringComparison.Ordinal);
        Assert.NotEmpty(rig.Audit.Events);
        Assert.All(rig.Audit.Events, e => Assert.DoesNotContain(key, e.ToString(), StringComparison.Ordinal));

        var registration = rig.Registry.List().Single();
        foreach (var property in typeof(AgentRegistration).GetProperties())
            Assert.NotEqual(key, property.GetValue(registration)?.ToString());

        var again = await rig.SendAsync(AgentRig.Post(
            AgentRig.ToolCall(3, "register_agent", $"{{\"pending_id\":\"{pendingId}\"}}")));
        Assert.DoesNotContain(key, again.Body, StringComparison.Ordinal);
        Assert.Equal("not_found", (string?)StructuredContent(again)["status"]);
    }

    /// <summary>
    /// Review finding 1: with the shipped 45-second wait, register_agent must answer "pending" with the fingerprint at
    /// once, so the agent can show it to the user before the user decides.
    /// </summary>
    [Fact]
    public async Task TestTheFingerprintReachesTheAgentBeforeTheUserDecides()
    {
        var shipped = new McpServerOptions().DecisionWait;
        Assert.Equal(TimeSpan.FromSeconds(45), shipped);
        var rig = new AgentRig(decisionWait: shipped);

        var call = rig.SendAsync(AgentRig.Post(AgentRig.ToolCall(1, "register_agent", "{\"name\":\"Claude Code\"}")));
        var first = await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(call, first);
        var data = StructuredContent(await call);
        Assert.Equal("pending", (string?)data["status"]);
        var pending = rig.Registry.FindPending((string)data["pending_id"]!)!;
        Assert.Equal(PendingRegistrationStatus.Pending, pending.Status);
        Assert.Equal(pending.Fingerprint, (string?)data["fingerprint"]);
    }

    /// <summary>
    /// Review finding 2: a poll whose client hung up must not collect the key — nothing is issued, stored or written,
    /// and the approved registration stays collectable by the agent's next poll.
    /// </summary>
    [Fact]
    public async Task TestAKeyIsNeverIssuedToAPollWhoseClientHasGone()
    {
        var rig = new AgentRig(decisionWait: TimeSpan.FromSeconds(30));
        var pending = rig.Registry.Begin("Claude Code", null, null).Pending!;
        var connection = new DuplexStream(System.Text.Encoding.UTF8.GetBytes(AgentRig.Post(
            AgentRig.ToolCall(2, "register_agent", $"{{\"pending_id\":\"{pending.PendingId}\"}}"))));

        var poll = rig.Endpoint.HandleConnectionAsync(connection, System.Net.IPAddress.Loopback, CancellationToken.None);
        await Task.Delay(200);
        connection.CloseFromClient();
        await Task.WhenAny(poll, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(rig.Registry.Decide(pending.PendingId, approve: true));
        await poll.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.DoesNotContain(rig.Audit.Events, e => e.Kind == AgentAuditKind.KeyIssued);
        Assert.Null(rig.Store.Serialized);
        Assert.Equal(0, connection.Output.Length);

        var collected = await rig.SendAsync(AgentRig.Post(
            AgentRig.ToolCall(3, "register_agent", $"{{\"pending_id\":\"{pending.PendingId}\"}}")));
        Assert.Equal("approved", (string?)StructuredContent(collected)["status"]);
    }

    [Fact]
    public async Task TestIssuedKeyIsRedactedWhenFormatted()
    {
        var rig = new AgentRig();
        var outcome = rig.Registry.Begin("Agent", null, null);
        rig.Registry.Decide(outcome.Pending!.PendingId, approve: true);

        var result = await rig.Registry.WaitAsync(outcome.Pending.PendingId, TimeSpan.Zero, CancellationToken.None);
        var key = result.Key!.Reveal();

        Assert.DoesNotContain(key, result.Key.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(key, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(key, $"{result.Key}", StringComparison.Ordinal);
    }

    [Fact]
    public void TestRevokedKeyStopsAuthenticatingImmediately()
    {
        var rig = new AgentRig();
        var (registration, key) = rig.RegisterApproved();
        AgentRegistration? revoked = null;
        rig.Registry.Revoked += r => revoked = r;

        Assert.True(rig.Registry.Revoke(registration.Id));

        Assert.Null(rig.Registry.Authenticate(key));
        Assert.Equal(registration.Id, revoked!.Id);
        Assert.Empty(AgentRegistrationSerializer.Deserialize(rig.Store.Serialized));
        Assert.Contains(rig.Audit.Events, e => e.Kind == AgentAuditKind.RegistrationRevoked && e.AgentId == registration.Id);
    }

    [Fact]
    public void TestKeyAuthenticatesOnlyForTheUserWhoApprovedIt()
    {
        var rig = new AgentRig();
        var (_, key) = rig.RegisterApproved();

        rig.Registry.SetUser(AgentRig.UserId + 1);
        Assert.Null(rig.Registry.Authenticate(key));
        Assert.Empty(rig.Registry.List());

        rig.Registry.SetUser(null);
        Assert.Null(rig.Registry.Authenticate(key));

        rig.Registry.SetUser(AgentRig.UserId);
        Assert.NotNull(rig.Registry.Authenticate(key));
    }

    [Fact]
    public void TestRegistrationsSurviveARestartThroughTheStore()
    {
        var rig = new AgentRig();
        var (registration, key) = rig.RegisterApproved();

        var reloaded = new AgentRegistry(rig.Store, rig.Time, rig.Audit);
        reloaded.SetUser(AgentRig.UserId);

        Assert.Equal(registration.Id, reloaded.Authenticate(key)!.Id);
    }

    [Fact]
    public void TestRenamePersistsAndRefusesUnusableNames()
    {
        var rig = new AgentRig();
        var (registration, _) = rig.RegisterApproved();

        Assert.True(rig.Registry.Rename(registration.Id, "  Claude   on the laptop "));
        Assert.Equal("Claude on the laptop", rig.Registry.List().Single().Name);
        Assert.Contains("Claude on the laptop", rig.Store.Serialized!, StringComparison.Ordinal);

        Assert.False(rig.Registry.Rename(registration.Id, ""));
        Assert.False(rig.Registry.Rename(registration.Id, new string('n', AgentText.MaxNameLength + 1)));
        Assert.False(rig.Registry.Rename("no-such-agent", "Name"));
        Assert.Equal("Claude on the laptop", rig.Registry.List().Single().Name);
    }

    [Fact]
    public void TestRegistrationInputIsBoundedAndStrippedOfControlCharacters()
    {
        var rig = new AgentRig();

        Assert.Equal(RegistrationOutcomeKind.Invalid, rig.Registry.Begin(null, null, null).Kind);
        Assert.Equal(RegistrationOutcomeKind.Invalid,
            rig.Registry.Begin(new string('n', AgentText.MaxNameLength + 1), null, null).Kind);
        Assert.Equal(RegistrationOutcomeKind.Invalid,
            rig.Registry.Begin("Agent", new string('d', AgentText.MaxDescriptionLength + 1), null).Kind);

        // U+202E (right-to-left override) is how "evil" is made to read as "live" in a dialog.
        var outcome = rig.Registry.Begin("Good‮agent\u0007\nname", "line one\r\nline two", null);
        Assert.Equal(RegistrationOutcomeKind.Created, outcome.Kind);
        Assert.Equal("Good agent name", outcome.Pending!.Name);
        Assert.Equal("line one line two", outcome.Pending.Description);
    }

    [Fact]
    public void TestRegistrationAttemptsAreRateLimited()
    {
        var rig = new AgentRig(new AgentRegistryOptions { MaxAttemptsPerWindow = 2, MaxPending = 10 });

        Assert.Equal(RegistrationOutcomeKind.Created, rig.Registry.Begin("One", null, null).Kind);
        Assert.Equal(RegistrationOutcomeKind.Created, rig.Registry.Begin("Two", null, null).Kind);
        var limited = rig.Registry.Begin("Three", null, null);

        Assert.Equal(RegistrationOutcomeKind.RateLimited, limited.Kind);
        Assert.True(limited.RetryAfter > TimeSpan.Zero);

        rig.Time.Advance(new AgentRegistryOptions().RateWindow);
        Assert.Equal(RegistrationOutcomeKind.Created, rig.Registry.Begin("Four", null, null).Kind);
    }

    [Fact]
    public void TestAFloodCollapsesIntoTheCounterOfTheOneDialog()
    {
        var rig = new AgentRig(new AgentRegistryOptions { MaxPending = 1 });
        var first = rig.Registry.Begin("Claude Code", null, new AgentClientInfo("claude-code", "2"));

        var overCap = rig.Registry.Begin("Other agent", null, null);
        var duplicate = rig.Registry.Begin("claude code", null, new AgentClientInfo("Claude-Code", "2"));

        Assert.Equal(RegistrationOutcomeKind.RateLimited, overCap.Kind);
        Assert.Equal(RegistrationOutcomeKind.AlreadyPending, duplicate.Kind);
        Assert.Null(duplicate.Pending);
        var prompt = rig.Registry.GetPrompt(first.Pending!.PendingId)!;
        Assert.Equal(2, prompt.RefusedCount);
        Assert.Equal(0, prompt.WaitingCount);
    }

    [Fact]
    public async Task TestApprovalDialogsAreShownOneAtATime()
    {
        var answer = new TaskCompletionSource<bool>();
        var prompter = new ScriptedPrompter { OnRegistration = _ => answer.Task };
        var rig = new AgentRig(new AgentRegistryOptions { MaxPending = 3 }, prompter: prompter);
        rig.Approvals.Start();

        var first = rig.Registry.Begin("First", null, null).Pending!;
        var second = rig.Registry.Begin("Second", null, null).Pending!;
        rig.Approvals.EnqueueRegistration(first.PendingId);
        rig.Approvals.EnqueueRegistration(second.PendingId);
        await WaitUntil(() => prompter.Shown == 1);
        await Task.Delay(100);

        Assert.Equal(1, prompter.Shown);
        Assert.Equal(1, prompter.RegistrationPrompts.Single().WaitingCount);

        answer.SetResult(true);
        await WaitUntil(() => prompter.Shown == 2);
        Assert.Equal(1, prompter.MaxOnScreen);
        Assert.Equal(PendingRegistrationStatus.Approved, first.Status);
        await rig.Approvals.StopAsync();
    }

    [Fact]
    public async Task TestUndecidedRegistrationExpiresAndUncollectedKeyIsDiscarded()
    {
        var rig = new AgentRig();
        var undecided = rig.Registry.Begin("Slow user", null, null).Pending!;
        var uncollected = rig.Registry.Begin("Gone agent", null, null).Pending!;
        rig.Registry.Decide(uncollected.PendingId, approve: true);

        rig.Time.Advance(new AgentRegistryOptions().PendingTimeout);
        rig.Registry.Sweep();

        Assert.Equal(RegistrationResultKind.Expired,
            (await rig.Registry.WaitAsync(undecided.PendingId, TimeSpan.Zero, CancellationToken.None)).Kind);
        Assert.Equal(RegistrationResultKind.NotFound,
            (await rig.Registry.WaitAsync(uncollected.PendingId, TimeSpan.Zero, CancellationToken.None)).Kind);
        Assert.Null(rig.Store.Serialized);
        Assert.Contains(rig.Audit.Events, e => e.Kind == AgentAuditKind.RegistrationAbandoned);
    }

    [Fact]
    public async Task TestDeniedRegistrationIssuesNoKey()
    {
        var rig = new AgentRig();
        var pending = rig.Registry.Begin("Unwanted", null, null).Pending!;

        rig.Registry.Decide(pending.PendingId, approve: false);
        var result = await rig.Registry.WaitAsync(pending.PendingId, TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(RegistrationResultKind.Denied, result.Kind);
        Assert.Null(result.Key);
        Assert.Null(rig.Store.Serialized);
    }

    private static JsonObject StructuredContent(RawResponse response)
    {
        Assert.Equal(200, response.Status);
        var root = JsonNode.Parse(response.Body)!.AsObject();
        return root["result"]!["structuredContent"]!.AsObject();
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }
}

/// <summary>Reads the agent core's source, for the guards that are about how the code is written.</summary>
internal static class AgentSource
{
    public static string Read(string fileName) => File.ReadAllText(Path.Combine(CoreDirectory(), fileName));

    public static string[] CoreFiles() => Directory.GetFiles(CoreDirectory(), "*.cs");

    private static string CoreDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var core = Path.Combine(directory.FullName, "GUIClient", "Agents", "Core");
            if (Directory.Exists(core)) return core;
        }
        throw new InvalidOperationException("Could not find GUIClient/Agents/Core walking up from " + AppContext.BaseDirectory);
    }
}
