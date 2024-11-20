using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ServerServices.Interfaces;
using ServerServices.Services;
using ServerServices.Tests.DI;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

public class CommentsServiceTests: BaseServiceTest
{

    private readonly ICommentsService _commentsService;
    
    public CommentsServiceTests()
    {
        _commentsService = _serviceProvider.GetRequiredService<ICommentsService>();
    }
    
    [Fact]
    public async Task TestGet()
    {
        // Arrange


        // Act
        // Call the method you're testing.
        
        var all = await _commentsService.GetCommentsAsync("FixRequest");

        // Assert
        // Verify the results.
        
        Assert.NotNull(all);
        Assert.NotEmpty(all);
        Assert.Equal(2, all.Count);
    }
    
    [Fact]
    public async Task TestGetFixRequest()
    {
        // Arrange

        // Act
        // Call the method you're testing.

        var all = await _commentsService.GetFixRequestCommentsAsync(1);

        // Assert
        // Verify the results.
        
        Assert.NotNull(all);
        Assert.NotEmpty(all);
        //Assert.Equal(1, all.Count);
        Assert.Single(all);
    }
    
    [Fact]
    public async Task TestGetUserComments()
    {
        // Arrange

        // Act
        // Call the method you're testing.

        var all = await _commentsService.GetUserCommentsAsync(1);

        // Assert
        // Verify the results.
        
        Assert.NotNull(all);
        Assert.NotEmpty(all);
        Assert.Equal(2, all.Count);
    }
    
    [Fact]
    public async Task TestGetFixRequestComments()
    {
        // Arrange

        // Act
        // Call the method you're testing.

        var all = await _commentsService.GetFixRequestCommentsAsync(1);

        // Assert
        // Verify the results.
        
        Assert.NotNull(all);
        Assert.NotEmpty(all);
        //Assert.Equal(1, all.Count);
        Assert.Single(all);
    }
    
    [Fact]
    public async Task TestCreate()
    {
        // Arrange


        // Act
        // Call the method you're testing.
        await _commentsService.CreateCommentsAsync(1, 
            DateTime.Now, null, "FixRequest", false, "Name", "Text", 1, null, null, null);

        var all = await _commentsService.GetCommentsAsync("FixRequest");
        // Assert
        // Verify the results.
        
        Assert.Equal(4, all.Count);

    }
    
}