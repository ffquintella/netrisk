using System;
using Microsoft.Extensions.DependencyInjection;
using ServerServices.Interfaces;
using ServerServices.Services;
using ServerServices.Tests.DI;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

public class CommentsServiceTests
{
    private readonly IServiceProvider _serviceProvider = ServiceRegistration.GetServiceProvider();
    
    [Fact]
    public async void TestMethod1()
    {
        // Arrange
        var commentsService = _serviceProvider.GetRequiredService<ICommentsService>();

        // Act
        // Call the method you're testing.
        
        var all = await commentsService.GetCommentsAsync("FixRequest");

        // Assert
        // Verify the results.
        
        Assert.NotNull(all);
        Assert.NotEmpty(all);
        Assert.Equal(2, all.Count);
    }
    
}