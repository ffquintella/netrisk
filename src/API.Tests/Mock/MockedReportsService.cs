using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Entities;
using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

/// <summary>
/// The 2.1 reporting engine, stubbed at the seam that matters to a controller: <c>CreateAsync</c>
/// echoes the report back with an id and a file id, as the real service does once it has rendered
/// and stored the artifact.
///
/// It echoes rather than returning a canned row on purpose — the governance evidence export (8.4.2)
/// is asserted on the type and parameters it hands the engine, and a mock that discarded them would
/// let a controller build the wrong report and still pass.
/// </summary>
public static class MockedReportsService
{
    public static IReportsService Create()
    {
        var service = Substitute.For<IReportsService>();

        service.GetAll().Returns([]);

        service.CreateAsync(Arg.Any<Report>(), Arg.Any<User>())
            .Returns(call =>
            {
                var report = call.ArgAt<Report>(0);
                report.Id = report.Id == 0 ? 1 : report.Id;
                report.FileId = 1;
                return Task.FromResult(report);
            });

        return service;
    }
}
