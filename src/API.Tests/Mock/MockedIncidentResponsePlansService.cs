using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

public class MockedIncidentResponsePlansService
{
    public static IIncidentResponsePlansService Create()
    {
        var incidentResponsePlansService = Substitute.For<IIncidentResponsePlansService>();
        return incidentResponsePlansService;
    }
}