using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace ServerServices.Tests.Mock;

public static class MockConfiguration
{
    public static IConfiguration Create()
    {
        var config = Substitute.For<IConfiguration>();
        
        config["website:protocol"].Returns("http");
        config["website:host"].Returns("localhost");
        config["website:port"].Returns("5000");

        return config;
    }
}