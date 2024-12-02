using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DAL.Entities;
using Model.Exceptions;
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
        
        mockClient.ExecuteAsync( Arg.Is<RestRequest>(rq => rq.Resource == "/IncidentResponsePlans" && rq.Method == Method.Put), Arg.Any<CancellationToken>())
            .Returns(new RestResponse
            {
                StatusCode = HttpStatusCode.OK,
                ResponseStatus = ResponseStatus.Completed,
                Content = JsonSerializer.Serialize(new IncidentResponsePlan
                {
                    Id = 1,
                    Name = "TestUpdate",
                    Description = "Test"
                }),
                ContentType = "application/json",
                ContentLength = 2
            });
        
        mockClient.ExecuteAsync( Arg.Is<RestRequest>(rq => rq.Resource == "/IncidentResponsePlans/1" && rq.Method == Method.Delete), Arg.Any<CancellationToken>())
            .Returns(new RestResponse
            {
                StatusCode = HttpStatusCode.OK,
                ResponseStatus = ResponseStatus.Completed
            });
        
        mockClient.ExecuteAsync( Arg.Is<RestRequest>(rq => rq.Resource == "/IncidentResponsePlans/1" && rq.Method == Method.Get), Arg.Any<CancellationToken>())
            .Returns(new RestResponse
            {
                StatusCode = HttpStatusCode.OK,
                ResponseStatus = ResponseStatus.Completed,
                Content = JsonSerializer.Serialize(GetIRPs()[0])  ,
                ContentType = "application/json",
                ContentLength = 2
            });
        
        mockClient.ExecuteAsync( Arg.Is<RestRequest>(rq => rq.Resource == "/IncidentResponsePlans/1/Tasks" && rq.Method == Method.Post), Arg.Any<CancellationToken>())
            .Returns(async (callInfo) =>
            {
                var rq = callInfo.Arg<RestRequest>();

                var bodyParameter = rq!.Parameters.FirstOrDefault(p => p.Type == ParameterType.RequestBody);
                var body = (IncidentResponsePlanTask) bodyParameter!.Value!;

                body!.Id = 1;

                var response = new RestResponse
                {
                    StatusCode = HttpStatusCode.Created,
                    ResponseStatus = ResponseStatus.Completed,
                    Content = JsonSerializer.Serialize(body),
                    ContentType = "application/json",
                    ContentLength = 2
                };
                
                return await Task.FromResult(response);
            });
        
        mockClient.ExecuteAsync( Arg.Is<RestRequest>(rq => rq.Resource == "/IncidentResponsePlans/1/Tasks/1" && rq.Method == Method.Put), Arg.Any<CancellationToken>())
            .Returns(new RestResponse
            {
                StatusCode = HttpStatusCode.OK,
                ResponseStatus = ResponseStatus.Completed,
                Content = JsonSerializer.Serialize(new IncidentResponsePlanTask
                {
                    Id = 1,
                    PlanId = 1,
                    Description = "Test"
                }),
                ContentType = "application/json",
                ContentLength = 2
            });
        
        /*mockClient.ExecuteAsync( Arg.Is<RestRequest>(rq => rq.Resource == "/IncidentResponsePlans/1/Tasks/1" && rq.Method == Method.Get), Arg.Any<CancellationToken>())
            .Returns(new RestResponse
            {
                StatusCode = HttpStatusCode.OK,
                ResponseStatus = ResponseStatus.Completed,
                Content = JsonSerializer.Serialize(GetIRPs()[0].Tasks.ToList()[0]),
                ContentType = "application/json",
                ContentLength = 2
            });*/
        
        mockClient.ExecuteAsync( Arg.Is<RestRequest>(rq => rq.Resource == "/IncidentResponsePlans/1/Tasks/1" && rq.Method == Method.Delete), Arg.Any<CancellationToken>())
            .Returns(new RestResponse
            {
                StatusCode = HttpStatusCode.OK,
                ResponseStatus = ResponseStatus.Completed
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