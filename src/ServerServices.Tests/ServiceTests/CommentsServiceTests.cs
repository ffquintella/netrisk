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
        
        Assert.Equal(3, all.Count);

    }

    [Fact]
    public async Task TestCreateHostCommentIsListedForThatHostOnly()
    {
        await _commentsService.CreateCommentsAsync(1,
            DateTime.Now, null, "Host", false, "Name", "Patched last night", null, null, null, 1);
        await _commentsService.CreateCommentsAsync(1,
            DateTime.Now, null, "Host", false, "Name", "Other host", null, null, null, 2);

        var onHost1 = await _commentsService.GetHostCommentsAsync(1);

        var comment = Assert.Single(onHost1);
        Assert.Equal("Patched last night", comment.Text);
        Assert.Equal(1, comment.HostId);
    }

    [Fact]
    public async Task TestCreateHostCommentRequiresHostId()
    {
        await Assert.ThrowsAsync<Exception>(() => _commentsService.CreateCommentsAsync(1,
            DateTime.Now, null, "Host", false, "Name", "Text", null, null, null, null));
    }

    [Fact]
    public async Task TestCreateHostCommentRequiresText()
    {
        await Assert.ThrowsAsync<Exception>(() => _commentsService.CreateCommentsAsync(1,
            DateTime.Now, null, "Host", false, "Name", "  ", null, null, null, 1));
    }

    [Fact]
    public async Task TestCreateHostCommentRejectsUnknownHost()
    {
        await Assert.ThrowsAsync<Exception>(() => _commentsService.CreateCommentsAsync(1,
            DateTime.Now, null, "Host", false, "Name", "Text", null, null, null, 99999));
    }

}