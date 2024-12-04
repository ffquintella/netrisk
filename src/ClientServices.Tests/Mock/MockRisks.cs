using System.Net;
using System.Text.Json;
using System.Threading;
using DAL.Entities;
using NSubstitute;
using RestSharp;

namespace ClientServices.Tests.Mock;

public static class MockRisks
{
    public static void ConfigureMocks(ref IRestClient mockClient)
    {
        mockClient.ExecuteAsync( Arg.Is<RestRequest>(rq => rq.Resource == "/Risks/1/IncidentResponsePlan"), Arg.Any<CancellationToken>())
            .Returns(new RestResponse
            {
                StatusCode = HttpStatusCode.OK,
                ResponseStatus = ResponseStatus.Completed,
                Content = JsonSerializer.Serialize(new IncidentResponsePlan
                {
                    Id = 1,
                    Name = "Test",
                    Description = "Test"
                }),
                ContentType = "application/json",
                ContentLength = 2
            });
    }
}