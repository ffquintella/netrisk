using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Http;
using ClientServices.Services;
using JetBrains.Annotations;
using Model.Agents;
using Xunit;

namespace ClientServices.Tests.Http;

/// <summary>
/// T358 (S55 "Audit and attribution"): a call an agent tool makes carries <c>X-NetRisk-Agent</c> with the agent's
/// registration id; the user's own calls on the same services never do, and a caller cannot put its own value there.
/// The chain tested is the production one (<see cref="RestService.ComposeHandlers"/>).
/// </summary>
[TestSubject(typeof(AgentContextHandler))]
public class AgentContextHandlerTest
{
    private const string Registration = "Qm9vdHN0cmFwSWQx_-0123";

    private sealed class Recorder : HttpMessageHandler
    {
        public List<string?> Seen { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen.Add(request.Headers.TryGetValues(AgentContextHeader.Name, out var values) ? string.Join("|", values) : null);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static async Task<string?> SendAsync(Recorder recorder, Action<HttpRequestMessage>? shape = null)
    {
        using var client = new HttpClient(RestService.ComposeHandlers(recorder, null), disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Put, "https://127.0.0.1:5443/Risks/1");
        shape?.Invoke(request);
        await client.SendAsync(request);
        return recorder.Seen.Last();
    }

    [Fact]
    public async Task TestACallInsideAnAgentScopeCarriesTheRegistrationId()
    {
        var recorder = new Recorder();
        using (AgentCallContext.Begin(Registration))
            Assert.Equal(Registration, await SendAsync(recorder));
    }

    [Fact]
    public async Task TestTheUsersOwnCallsCarryNoHeader()
    {
        var recorder = new Recorder();
        Assert.Null(await SendAsync(recorder));
        using (AgentCallContext.Begin(Registration)) await SendAsync(recorder);
        Assert.Null(await SendAsync(recorder)); // the scope ended with the call
    }

    [Fact]
    public async Task TestACallerCannotPutItsOwnValueInTheHeader()
    {
        var recorder = new Recorder();
        Assert.Null(await SendAsync(recorder, r => r.Headers.TryAddWithoutValidation(AgentContextHeader.Name, "forged-agent-id")));
        using (AgentCallContext.Begin(Registration))
            Assert.Equal(Registration, await SendAsync(recorder,
                r => r.Headers.TryAddWithoutValidation(AgentContextHeader.Name, "forged-agent-id")));
    }

    [Fact]
    public async Task TestTheScopeFlowsIntoAwaitedWorkButNotIntoAnUnrelatedFlow()
    {
        var recorder = new Recorder();
        var outside = Task.Run(async () =>
        {
            await Task.Delay(20);
            return await SendAsync(recorder);
        });
        string? inside;
        using (AgentCallContext.Begin(Registration))
        {
            await Task.Yield();
            inside = await Task.Run(() => SendAsync(recorder));
        }

        Assert.Equal(Registration, inside);
        Assert.Null(await outside);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("has space in it")]
    [InlineData("line\r\nX-Injected: 1")]
    [InlineData("ana;role=admin")]
    [InlineData("Qm9vdHN0cmFwSWQx_-012")]     // 21: one short of an issued id
    [InlineData("Qm9vdHN0cmFwSWQx_-01234")]   // 23
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 64: accepted before review M63 #1
    public void TestAValueThatIsNotARegistrationIdIsRefused(string value)
    {
        Assert.Throws<ArgumentException>(() => AgentCallContext.Begin(value));
        Assert.Null(AgentContextHeader.Parse(value));
    }
}
