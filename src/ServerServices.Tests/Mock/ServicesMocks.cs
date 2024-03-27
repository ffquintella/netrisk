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
    
    public static Mock<DALService> GetDALServiceMock()
    {
        var dsmoq =  new Mock<DALService>();

        return dsmoq;
    }
}