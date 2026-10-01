using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClientServices.Tests.Services;

[TestSubject(typeof(CommentsRestService))]
public class CommentsRestServiceTest: BaseServiceTest
{
    private readonly ICommentsService _commentsService;
    
    public CommentsRestServiceTest()
    {
        _commentsService = _serviceProvider.GetRequiredService<ICommentsService>();
    }
    
    [Fact]
    public async Task TestGetFixRequestComments()
    {
        // Arrange
        
        // Act
        // Call the method you're testing.
        
        var hosts = await _commentsService.GetFixRequestCommentsAsync(1);
        
        // Assert
        // Verify the results.
        
        Assert.NotNull(hosts);


    }
    [Fact]
    public async Task TestGetHostComments()
    {
        var comments = await _commentsService.GetHostCommentsAsync(1);

        Assert.Equal(2, comments.Count);
        Assert.All(comments, c => Assert.Equal(1, c.HostId));
    }
}