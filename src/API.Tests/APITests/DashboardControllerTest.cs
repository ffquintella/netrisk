using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using API.Controllers;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.Dashboard;
using Xunit;

namespace API.Tests.APITests;

[TestSubject(typeof(DashboardController))]
public class DashboardControllerTest : BaseControllerTest
{
    private readonly DashboardController _controller;

    public DashboardControllerTest()
    {
        _controller = _serviceProvider.GetRequiredService<DashboardController>();
    }

    [Fact]
    public async Task TestGetMasterDashboard()
    {
        var result = await _controller.GetMasterDashboard();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dashboard = Assert.IsType<MasterDashboard>(ok.Value);

        Assert.Equal(2, dashboard.Entities.Count);
        Assert.Equal(3, dashboard.Totals.OpenRisks);
    }

    [Fact]
    public async Task TestRefreshBypassesTheCache()
    {
        var cached = await _controller.GetMasterDashboard();
        var cachedValue = Assert.IsType<MasterDashboard>(Assert.IsType<OkObjectResult>(cached.Result).Value);
        Assert.True(cachedValue.FromCache);

        var refreshed = await _controller.GetMasterDashboard(refresh: true);
        var refreshedValue = Assert.IsType<MasterDashboard>(Assert.IsType<OkObjectResult>(refreshed.Result).Value);
        Assert.False(refreshedValue.FromCache);
    }

    /// <summary>
    /// The milestone requires a non-admin caller to get a 403. That is enforced by the
    /// authorization policy, which a controller unit test runs inside of rather than through —
    /// so assert the gate is actually declared on the action.
    /// </summary>
    [Fact]
    public void TestMasterDashboardIsAdminOnly()
    {
        var action = typeof(DashboardController).GetMethod(nameof(DashboardController.GetMasterDashboard));
        Assert.NotNull(action);

        var authorize = action!.GetCustomAttributes<AuthorizeAttribute>().ToList();

        Assert.Contains(authorize, a => a.Policy == "RequireAdminOnly");
    }
}
