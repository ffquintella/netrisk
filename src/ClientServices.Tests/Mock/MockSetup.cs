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
    private static List<HostsService> GetHostsService()
    {
        var list = new List<HostsService> ()
        {
            new HostsService
            {
                Id = 1,
                HostId = 1,
                Name = "Service 1",
                Port = 443,
                Protocol = "tcp"
            },
            new HostsService
            {
                Id = 2,
                HostId = 1,
                Name = "Service 2",
                Port = 80,
                Protocol = "tcp"
            },
        };

        return list;

    }
    
    public static IRestClient GetRestClient()
    {
        //var mockClient = new MockedRestClient();
        //mockClient.Responses.Add($"/Hosts/1/Services", GetHostsService());
        

        var mockClient = Substitute.For<IRestClient>();
        
        
        var defaultSerializer = new SerializerConfig();
        defaultSerializer.UseDefaultSerializers();
        var serializers = new RestSerializers(defaultSerializer);
        
        mockClient.Serializers.Returns(serializers);
        
        mockClient.ExecuteAsync(Arg.Any<RestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RestResponse
            {
                StatusCode = HttpStatusCode.OK,
                ResponseStatus = ResponseStatus.Completed,
                Content = JsonSerializer.Serialize(GetHostsService())  ,
                ContentType = "application/json",
                ContentLength = 2
            });
        
        
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