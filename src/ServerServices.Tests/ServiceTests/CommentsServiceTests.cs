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
    public async void TestGet()
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
    
    [Fact]
    public async void TestCreate()
    {
        // Arrange
        var commentsService = _serviceProvider.GetRequiredService<ICommentsService>();

        // Act
        // Call the method you're testing.
        await commentsService.CreateCommentsAsync(1, 
            DateTime.Now, null, "FixRequest", false, "Name", "Text", 1, null, null, null);

        var all = await commentsService.GetCommentsAsync("FixRequest");
        // Assert
        // Verify the results.
        
        Assert.Equal(3, all.Count);

    }
    
}