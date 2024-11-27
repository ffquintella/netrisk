using System;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace API.Tests.Mock;

public class MockedHttpContextAccessor
{
    public static IHttpContextAccessor Create()
    {
        //return new HttpContextAccessor();
        
        var claims = new[] { new Claim(ClaimTypes.Name, "testUser") };
        var identity = new ClaimsIdentity(claims, "Basic");
        var claimsPrincipal = new ClaimsPrincipal(identity);
        
        var mockHttpAccessor = Substitute.For<IHttpContextAccessor>();
        var context = new DefaultHttpContext
        {
            Connection =
            {
                Id = Guid.NewGuid().ToString(),
                LocalIpAddress = new IPAddress(new byte[] { 127, 0, 0, 1 }),
                RemoteIpAddress = new IPAddress(new byte[] { 127, 0, 0, 2 }),
            },
            User = claimsPrincipal
        };
        
        mockHttpAccessor.HttpContext.Returns(context);

        return mockHttpAccessor;
    }
}