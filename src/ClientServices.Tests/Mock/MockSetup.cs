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
    
    
 

    public static IRestClient GetRestClient()
    {
        
        var mockClient = Substitute.For<IRestClient>();
        
        var defaultSerializer = new SerializerConfig();
        defaultSerializer.UseDefaultSerializers();
        var serializers = new RestSerializers(defaultSerializer);
        
        mockClient.Serializers.Returns(serializers);
        
        MockComments.ConfigureMocks(mockClient: ref mockClient);
        MockHostsRestService.ConfigureMocks(mockClient: ref mockClient);
        MockIncidentResponsePlan.ConfigureMocks(mockClient: ref mockClient);
        MockRisks.ConfigureMocks(mockClient: ref mockClient);
        
        return mockClient;
    }

    public static IRestService GetRestService()
    {
        var mockRestService = Substitute.For<IRestService>();

        var restClient = GetRestClient();

        // Arg.Any on every parameter, including reportErrorResponses: a stub that matched only the
        // default would return null for the services that ask for an error-reporting client.
        mockRestService.GetClient(Arg.Any<IAuthenticator>(), Arg.Any<bool>(), Arg.Any<bool>())
            .Returns(restClient as RestClient);
        mockRestService.GetReliableClient(Arg.Any<IAuthenticator>(), Arg.Any<bool>(), Arg.Any<bool>())
            .Returns(restClient as IRestClient);
        
        return mockRestService;
    }
}