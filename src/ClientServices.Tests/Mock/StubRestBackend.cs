using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using Polly;
using ReliableRestClient;
using ReliableRestClient.Exceptions;
using RestSharp;
using RestSharp.Authenticators;

namespace ClientServices.Tests.Mock;

/// <summary>One HTTP exchange the service under test performed, for asserting what it sent.</summary>
public sealed class RecordedRequest
{
    public string Method { get; init; } = "";
    public string Path { get; init; } = "";
    public string Query { get; init; } = "";
    public string Body { get; init; } = "";

    public override string ToString() => $"{Method} {Path}{Query}";
}

/// <summary>
/// A programmable HTTP backend for the REST services.
///
/// The services acquire their client through <see cref="IRestService"/>, whose
/// <c>GetClient()</c> returns a <b>concrete</b> <see cref="RestClient"/> — so it cannot be an
/// NSubstitute double, which is why the older <c>MockSetup</c> could only reach the handful of
/// methods that use <c>GetReliableClient()</c>. Instead this stubs the layer underneath: a real
/// RestClient over a fake <see cref="HttpMessageHandler"/>. Serialization, status handling and
/// RestSharp's extension methods all run for real, so a test exercises the same code paths
/// production does.
///
/// <c>ThrowOnAnyError</c> is left <c>false</c> by default so a non-2xx surfaces the way the services
/// expect — <c>GetAsync&lt;T&gt;</c> returns null and <c>ExecuteAsync</c> reports the status — rather
/// than throwing before the service can inspect it. Note that this is *not* what the production client
/// does: <c>RestService</c> sets <c>ThrowOnAnyError = true</c>, which is why a service's status-code
/// handling could be thoroughly tested here and still be unreachable in the running application. Set
/// <see cref="ThrowsOnErrorResponses"/> to model the real client.
/// </summary>
public sealed class StubRestBackend : IRestService, IDisposable
{
    private const string BaseUrl = "https://localhost:5443";

    private readonly Dictionary<string, Route> _routes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RecordedRequest> _requests = new();
    private readonly HttpClient _httpClient;

    private sealed class Route
    {
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = "";
        public Exception? Throws;
    }

    public StubRestBackend()
    {
        _httpClient = new HttpClient(new StubHandler(this)) { BaseAddress = new Uri(BaseUrl) };
    }

    /// <summary>
    /// Model the production client, which sets RestSharp's <c>ThrowOnAnyError</c>: a non-2xx is raised
    /// as an <see cref="System.Net.Http.HttpRequestException"/> carrying only "Request failed with
    /// status code X", before the caller can read the body.
    ///
    /// Off by default so the existing tests keep the behaviour they were written against. A service
    /// that has to report *why* the server refused something should be tested with this on, because
    /// that is the client it will actually be handed — unless it asks for an error-reporting one.
    /// </summary>
    public bool ThrowsOnErrorResponses { get; init; }

    /// <summary>
    /// Wraps the reliable client in the real <see cref="ReliableRestClientWrapper"/>, the way
    /// <c>RestService.GetReliableClient</c> does in production.
    ///
    /// Off by default, because most tests want a plain exchange per call. It exists because the
    /// retrying client was invisible here: the stub handed every caller a bare client, so a service
    /// that sent a non-idempotent POST through the retrying one looked identical to one that did not
    /// — and the difference, on an endpoint answering 5xx, was eleven Vision One synchronizations
    /// from one click.
    ///
    /// The policy is production's — <see cref="RestServerSideException"/>, <see cref="ReliableClientRetries"/>
    /// retries — with the backoff flattened to zero so a test stays fast. It used to be
    /// <c>Policy.NoOpAsync()</c>, on the reasoning that the wrapper's own attempt loop would supply the
    /// retries. That loop is the upstream defect this whole split exists because of
    /// (ffquintella/reliable-rest-client-wrapper#1): it ran up to eleven undelayed attempts inside each
    /// policy attempt. Leaving the no-op here meant the <c>…IsStillRetried</c> tests were asserting on
    /// the bug, and would have gone green-to-red the moment the submodule was fixed — reporting a
    /// repaired dependency as a regression in this repository.
    /// </summary>
    public bool RetriesLikeProduction { get; init; }

    /// <summary>
    /// The retry count <c>RestService.GetReliableClient</c> configures in production.
    ///
    /// Public so a test can assert an exact attempt count rather than <c>&gt; 1</c>. Note that the
    /// total is only <c>ReliableClientRetries + 1</c> once the submodule carries the fix above; until
    /// then the wrapper's inner loop multiplies it by eleven, which is why the current assertions are
    /// lower bounds.
    /// </summary>
    public const int ReliableClientRetries = 10;

    /// <summary>Every exchange performed so far, in order.</summary>
    public IReadOnlyList<RecordedRequest> Requests => _requests;

    /// <summary>
    /// The last exchange.
    ///
    /// Non-nullable, and throws rather than returning null when nothing has been sent. A test reading
    /// this has already asserted that a request went out — or is about to assert something about it —
    /// so "no request was sent" is a failure, and saying so beats a NullReferenceException fifty
    /// lines later. Declaring it nullable instead would push a null check onto some three hundred
    /// call sites to describe a state none of them wants.
    /// </summary>
    public RecordedRequest LastRequest => _requests.Count > 0
        ? _requests[^1]
        : throw new InvalidOperationException(
            "No request has been sent through the stub backend yet.");

    private static string Key(Method method, string path) => $"{method.ToString().ToUpperInvariant()} {path}";

    /// <summary>Answers <paramref name="path"/> with <paramref name="body"/> serialized as JSON.</summary>
    public StubRestBackend On(Method method, string path, object body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes[Key(method, path)] = new Route
        {
            Status = status,
            Body = body is string raw ? raw : JsonSerializer.Serialize(body)
        };
        return this;
    }

    /// <summary>Answers <paramref name="path"/> with a bare status and no body.</summary>
    public StubRestBackend OnStatus(Method method, string path, HttpStatusCode status)
    {
        _routes[Key(method, path)] = new Route { Status = status, Body = "" };
        return this;
    }

    /// <summary>
    /// Makes the transport itself fail, which is what drives the services'
    /// <c>catch (HttpRequestException)</c> branches.
    /// </summary>
    public StubRestBackend OnTransportFailure(Method method, string path,
        Exception? exception = null)
    {
        _routes[Key(method, path)] = new Route
        {
            Throws = exception ?? new HttpRequestException("simulated transport failure")
        };
        return this;
    }

    public StubRestBackend OnGet(string path, object body, HttpStatusCode status = HttpStatusCode.OK)
        => On(Method.Get, path, body, status);

    public StubRestBackend OnPost(string path, object body, HttpStatusCode status = HttpStatusCode.OK)
        => On(Method.Post, path, body, status);

    public StubRestBackend OnPut(string path, object body, HttpStatusCode status = HttpStatusCode.OK)
        => On(Method.Put, path, body, status);

    public StubRestBackend OnDelete(string path, object body, HttpStatusCode status = HttpStatusCode.OK)
        => On(Method.Delete, path, body, status);

    /// <summary>True when the service sent exactly one request and it matched.</summary>
    public bool Sent(Method method, string path) =>
        _requests.Any(r => r.Method.Equals(method.ToString(), StringComparison.OrdinalIgnoreCase) && r.Path == path);

    // A path with no configured route answers 501, which is what the previous mock did for an
    // unexpected call. That keeps an unstubbed route a visible failure rather than a silent 200.
    private Route Resolve(string path, string method)
    {
        return _routes.TryGetValue($"{method.ToUpperInvariant()} {path}", out var route)
            ? route
            : new Route { Status = HttpStatusCode.NotImplemented, Body = "" };
    }

    private sealed class StubHandler(StubRestBackend backend) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var body = request.Content == null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);

            backend._requests.Add(new RecordedRequest
            {
                Method = request.Method.Method,
                Path = uri.AbsolutePath,
                Query = uri.Query,
                Body = body
            });

            var route = backend.Resolve(uri.AbsolutePath, request.Method.Method);

            if (route.Throws != null) throw route.Throws;

            return new HttpResponseMessage(route.Status)
            {
                Content = new StringContent(route.Body, Encoding.UTF8, "application/json")
            };
        }
    }

    private RestClient NewClient(bool reportErrorResponses)
    {
        // disposeHttpClient: false — the services all wrap their client in `using`, and disposing
        // the shared HttpClient on the first call would break every later one.
        return new RestClient(
            _httpClient,
            new RestClientOptions(BaseUrl)
            {
                ThrowOnAnyError = ThrowsOnErrorResponses && !reportErrorResponses
            },
            disposeHttpClient: false);
    }

    public RestClient GetClient(IAuthenticator? autenticator = null,
        bool ignoreTimeVerification = false, bool reportErrorResponses = false)
        => NewClient(reportErrorResponses);

    public IRestClient GetReliableClient(IAuthenticator? autenticator = null,
        bool ignoreTimeVerification = false, bool reportErrorResponses = false)
        => RetriesLikeProduction
            ? new ReliableRestClientWrapper(NewClient(reportErrorResponses), RetryLikeProduction())
            : NewClient(reportErrorResponses);

    /// <summary>
    /// The policy <c>RestService.GetReliableClient</c> builds, minus the wait.
    ///
    /// Same handled exception and same retry count, because those are what decide whether a request is
    /// repeated and are therefore what a test is measuring. The backoff is the one thing deliberately
    /// not copied: production ramps to 1000 * 2^10 ms, so a single faithful read test would sit here
    /// for the better part of an hour.
    /// </summary>
    private static IAsyncPolicy RetryLikeProduction()
        => Policy.Handle<RestServerSideException>()
                 .WaitAndRetryAsync(ReliableClientRetries, _ => TimeSpan.Zero);

    public void Dispose() => _httpClient.Dispose();
}
