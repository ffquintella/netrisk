using Serilog;
using Moq;
using ServerServices.Services;

namespace ServerServices.Tests.Mock;

public static class ServicesMocks
{
    public static Mock<ILogger> GetLoggerMock()
    {
        return new Mock<ILogger>();
    }
    
    public static Mock<DalService> GetDALServiceMock()
    {
        var dsmoq =  new Mock<DalService>();

        return dsmoq;
    }
}