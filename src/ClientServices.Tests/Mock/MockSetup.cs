using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using NSubstitute;
using NSubstitute.Extensions;
using RestSharp;
using RestSharp.Authenticators;
using RestSharp.Serializers;

namespace ClientServices.Tests.Mock;

public static class MockSetup
{
    
    
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

    public static IRestClient GetRestClient()
    {
        //var mockClient = new MockedRestClient();
        //mockClient.Responses.Add($"/Hosts/1/Services", GetHostsService());
        
        var restRequest = new RestRequest
        {
            Resource = "/Hosts/1/Services",
            Method = Method.Get,
            RequestFormat = DataFormat.Json,
        };

        var mockClient = Substitute.For<IRestClient>();
        
        var defaultSerializer = new SerializerConfig();
        defaultSerializer.UseDefaultSerializers();
        var serializers = new RestSerializers(defaultSerializer);
        
        mockClient.Serializers.Returns(serializers);
        
        
        mockClient.ExecuteAsync(Arg.Is<RestRequest>(rq => rq.Resource == "/Comments/fixrequest/1"), Arg.Any<CancellationToken>())
            .Returns(new RestResponse
            {
                StatusCode = HttpStatusCode.OK,
                ResponseStatus = ResponseStatus.Completed,
                Content = JsonSerializer.Serialize(GetFixRequestComments())  ,
                ContentType = "application/json",
                ContentLength = 2
            });
        
        MockHostsRestService.ConfigureMocks(mockClient: ref mockClient);
        MockIncidentResponsePlan.ConfigureMocks(mockClient: ref mockClient);
        
        
        return mockClient;
    }

    public static IRestService GetRestService()
    {
        var mockRestService = Substitute.For<IRestService>();

        var restClient = GetRestClient();

        mockRestService.GetClient(Arg.Any<IAuthenticator>(), Arg.Any<bool>()).Returns(restClient as RestClient);
        mockRestService.GetReliableClient(Arg.Any<IAuthenticator>(), Arg.Any<bool>()).Returns(restClient as IRestClient);
        
        return mockRestService;
    }
}