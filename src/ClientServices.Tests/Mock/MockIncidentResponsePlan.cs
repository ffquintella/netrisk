using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading;
using DAL.Entities;
using NSubstitute;
using RestSharp;

namespace ClientServices.Tests.Mock;

public static class MockIncidentResponsePlan
{
    public static void ConfigureMocks(ref IRestClient mockClient)
    {
        mockClient.ExecuteAsync(Arg.Is<RestRequest>(rq => rq.Resource == "/IncidentResponsePlans" && rq.Method == Method.Get), Arg.Any<CancellationToken>())
            .Returns(new RestResponse
            {
                StatusCode = HttpStatusCode.OK,
                ResponseStatus = ResponseStatus.Completed,
                Content = JsonSerializer.Serialize(GetIRPs())  ,
                ContentType = "application/json",
                ContentLength = 2
            });
        
        mockClient.ExecuteAsync( Arg.Is<RestRequest>(rq => rq.Resource == "/IncidentResponsePlans" && rq.Method == Method.Post), Arg.Any<CancellationToken>())
            .Returns(new RestResponse
            {
                StatusCode = HttpStatusCode.Created,
                ResponseStatus = ResponseStatus.Completed,
                Content = JsonSerializer.Serialize(new IncidentResponsePlan
                {
                    Id = 1,
                    Name = "TestCreate",
                    Description = "Test"
                }),
                ContentType = "application/json",
                ContentLength = 2
            });
    }
    
    
    private static List<IncidentResponsePlan> GetIRPs()
    {
        var list = new List<IncidentResponsePlan>
        {
            new IncidentResponsePlan
            {
                Id = 1,
                Name = "IncidentResponsePlan1",
                Description = "IncidentResponsePlan1 Description",
            },
            new IncidentResponsePlan
            {
                Id = 2,
                Name = "IncidentResponsePlan2",
                Description = "IncidentResponsePlan2 Description",
            }
        };
        return list;
    }
}