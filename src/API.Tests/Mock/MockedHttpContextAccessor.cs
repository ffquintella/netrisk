using System;
using System.Net;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace API.Tests.Mock;

public class MockedHttpContextAccessor
{
    public static IHttpContextAccessor Create()
    {
        //return new HttpContextAccessor();
        
        var mockHttpAccessor = Substitute.For<IHttpContextAccessor>();
        var context = new DefaultHttpContext
        {
            Connection =
            {
                Id = Guid.NewGuid().ToString(),
                LocalIpAddress = new IPAddress(new byte[] { 127, 0, 0, 1 }),
                RemoteIpAddress = new IPAddress(new byte[] { 127, 0, 0, 2 }),
            }
        };
        
        mockHttpAccessor.HttpContext.Returns(context);

        return mockHttpAccessor;
    }
}