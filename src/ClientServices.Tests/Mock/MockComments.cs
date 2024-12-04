using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading;
using DAL.Entities;
using NSubstitute;
using RestSharp;

namespace ClientServices.Tests.Mock;

public static class MockComments
{
    public static void ConfigureMocks(ref IRestClient mockClient)
    {
        
        mockClient.ExecuteAsync(Arg.Is<RestRequest>(rq => rq.Resource == "/Comments/fixrequest/1"), Arg.Any<CancellationToken>())
            .Returns(new RestResponse
            {
                StatusCode = HttpStatusCode.OK,
                ResponseStatus = ResponseStatus.Completed,
                Content = JsonSerializer.Serialize(GetFixRequestComments())  ,
                ContentType = "application/json",
                ContentLength = 2
            });
        
    }
    
    private static List<Comment> GetFixRequestComments()
    {
        var list = new List<Comment>()
        {
            new ()
            {
                Id = 1,
                Type = "FixRequest",
                Text = "T1",
                FixRequestId = 1,
                UserId = 1,
                CommenterName = "Name1"
            },
            new ()
            {
                Id = 2,
                Type = "FixRequest",
                Text = "T2",
                FixRequestId = 1,
                UserId = 2,
                CommenterName = "Name2"
            }
        };
        return list;
    }
}