using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

public static class MockedMitigationsService
{
    public static IMitigationsService Create()
    {
        
        var mitigationsService = Substitute.For<IMitigationsService>();



        return mitigationsService;

    }
}