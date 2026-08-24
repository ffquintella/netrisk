using System.Threading.Tasks;
using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

/// <summary>
/// Starts nothing and reports job id 7.
///
/// A controller test's interest in a background job ends at "was one started and what is its id";
/// actually running it would drag the messaging, localization and jobs-table stack into a test about
/// an HTTP response.
/// </summary>
public static class MockedJobManager
{
    public const int JobId = 7;

    public static IJobManager Create()
    {
        var manager = Substitute.For<IJobManager>();

        manager.RunAndRegisterJob(Arg.Any<IJobRunner>()).Returns(Task.FromResult(JobId));

        return manager;
    }
}
