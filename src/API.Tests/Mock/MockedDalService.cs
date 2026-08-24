using DAL.Context;
using NSubstitute;
using ServerServices.Services;

namespace API.Tests.Mock;

/// <summary>
/// A DAL service that reports an unrestricted entity scope.
///
/// The Track 3 controllers read the caller's scope to decide which tenant new findings belong to.
/// Unrestricted is the right default for a controller test: it means "no single tenant to claim",
/// which is the path every non-scoped request takes. Entity scoping itself is covered where it is
/// enforced, in <c>ServerServices.Tests.EntityScopeEnforcementTest</c>.
/// </summary>
public static class MockedDalService
{
    public static IDalService Create()
    {
        var service = Substitute.For<IDalService>();

        service.GetCurrentEntityScope().Returns(EntityScope.Unrestricted);

        return service;
    }
}
