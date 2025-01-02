using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

public static class MockedIncidentsService
{
    public static IIncidentsService Create()
    {
        var incidentsService = Substitute.For<IIncidentsService>();
        
        
        return incidentsService;
    }
}