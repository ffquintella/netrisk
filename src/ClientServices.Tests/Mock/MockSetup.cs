using System;
using System.Collections.Generic;
using System.Net;
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
        
        /*Mock<IRestClient> restClient = new Mock<IRestClient>();
        restClient.Setup(c => c.ExecuteAsync<MyResult>(
                Moq.It.IsAny<IRestRequest>(), 
                Moq.It.IsAny<Action<IRestResponse<MyResult>, RestRequestAsyncHandle>>()))
            .Callback<IRestRequest, Action<IRestResponse<MyResult>, RestRequestAsyncHandle>>((request, callback) =>
            {
                var responseMock = new Mock<IRestResponse<MyResult>>();
                responseMock.Setup(r => r.Data).Returns(new MyResult() { Foo = "Bar" });
                callback(responseMock.Object, null);
            });*/

        var mockClient = Substitute.For<IRestClient>();
        
        /*mockClient.ExecuteAsync(Arg.Any<RestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RestResponse
            {
                StatusCode = HttpStatusCode.OK,
                ResponseStatus = ResponseStatus.Completed,
                Content = "[]",
                ContentType = "application/json",
                ContentLength = 2
            });*/
        
        var defaultSerializer = new SerializerConfig();
        defaultSerializer.UseDefaultSerializers();
        var serializers = new RestSerializers(defaultSerializer);
        
        mockClient.Serializers.Returns(serializers);
        
        mockClient.ExecuteAsync(Arg.Any<RestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RestResponse
            {
                StatusCode = HttpStatusCode.OK,
                ResponseStatus = ResponseStatus.Completed,
                Content = "[]",
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