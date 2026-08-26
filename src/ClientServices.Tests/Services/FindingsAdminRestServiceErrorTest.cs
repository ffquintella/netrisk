using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Model.Exceptions;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// A regression test for a defect Track 8 found in the Track 3 client while building its own.
///
/// <see cref="FindingsAdminRestService"/>'s write path used RestSharp's <c>PostAsync</c>/<c>PutAsync</c>
/// extensions, which call <c>ThrowIfError</c> internally. A 400 or 422 therefore arrived as an
/// <c>HttpRequestException</c> with the response body already discarded — so the service's own
/// "pass the server's explanation through" branch was unreachable, and every rejected write surfaced
/// as a generic transport failure. An operator who typed an invalid dedup configuration was told the
/// server could not be reached.
///
/// The fix is <c>ExecuteAsync</c>, which hands the response back. This test fails on the pre-fix code.
/// </summary>
[TestSubject(typeof(FindingsAdminRestService))]
public class FindingsAdminRestServiceErrorTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IFindingsAdminService _service;

    public FindingsAdminRestServiceErrorTest()
    {
        _service = ResolveWith<IFindingsAdminService>(_backend);
    }

    [Fact]
    public async Task ARejectedWriteKeepsTheServersExplanationRatherThanBecomingATransportError()
    {
        _backend.On(Method.Put, "/DedupConfigurations/nessus",
            new
            {
                error = "invalid_parameter",
                parameterName = "StrategyChain",
                message = "A dedup chain needs at least one strategy."
            },
            HttpStatusCode.BadRequest);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            _service.SaveDedupConfigurationAsync(new ScannerDedupConfiguration
            {
                Importer = "nessus", StrategyChain = string.Empty
            }));

        Assert.Contains("at least one strategy", ex.Message);
    }

    [Fact]
    public async Task AnUnknownTargetIsStillReportedAsNotFound()
    {
        _backend.OnStatus(Method.Put, "/DedupConfigurations/unknown", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            _service.SaveDedupConfigurationAsync(new ScannerDedupConfiguration
            {
                Importer = "unknown", StrategyChain = "HashBased"
            }));
    }

    [Fact]
    public async Task ARealTransportFailureIsStillATransportFailure()
    {
        _backend.OnTransportFailure(Method.Put, "/DedupConfigurations/nessus");

        await Assert.ThrowsAsync<RestComunicationException>(() =>
            _service.SaveDedupConfigurationAsync(new ScannerDedupConfiguration
            {
                Importer = "nessus", StrategyChain = "HashBased"
            }));
    }
}
