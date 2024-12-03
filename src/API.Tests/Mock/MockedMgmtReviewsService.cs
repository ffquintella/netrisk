using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

public static class MockedMgmtReviewsService
{
    public static IMgmtReviewsService Create()
    {

        var mgmtReviewsService = Substitute.For<IMgmtReviewsService>();
        
        
        return mgmtReviewsService;
    }
}