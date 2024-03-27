using System.Threading.Tasks;
using JetBrains.Annotations;
using Moq;
using Serilog;
using ServerServices.Services;
using ServerServices.Tests.Mock;
using Xunit;

namespace ServerServices.Tests.Services;

[TestSubject(typeof(MessagesService))]
public class MessagesServiceTest
{
    private readonly Mock<ILogger> _mockLogger;
    private readonly Mock<DALService> _mockDalService;
    private readonly MessagesService _messagesService;

    public MessagesServiceTest()
    {
        _mockLogger = ServicesMocks.GetLoggerMock();
        _mockDalService = ServicesMocks.GetDALServiceMock();
        _messagesService = new MessagesService(_mockLogger.Object, _mockDalService.Object);
    }
    
    
    [Fact]
    public async Task SendMessageAsync_ShouldAddMessageToDatabase()
    {
        // Arrange
        var message = "Test message";
        var userId = 1;
        var chatId = 2;

        // Act
        await _messagesService.SendMessageAsync(message, userId, chatId);

        // Assert
        // Here you would assert that the message has been added to the database.
        // This will depend on how your DALService and database context are set up.
    }
}