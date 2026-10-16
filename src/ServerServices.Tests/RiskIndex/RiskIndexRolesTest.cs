using System.Linq;
using JetBrains.Annotations;
using Model.Entities;
using Model.RiskIndex;
using Xunit;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// T293 (S40 §11.1): the grant table the seed script is pinned to (<c>ConsoleClient.Tests/DB/RiskIndexSeedTest</c>). The
/// approver roles get the four permissions, the analyst authors but never approves, and Administrator gets nothing.
/// </summary>
[TestSubject(typeof(RiskIndexRoles))]
public class RiskIndexRolesTest
{
    [Fact]
    public void TestOnlyTheTwoApproverRolesAndTheAnalystAreSeeded()
    {
        Assert.Equal(new[] { RiskIndexRoles.RiskAdministrator, RiskIndexRoles.RiskAnalyst, RiskIndexRoles.RiskManager },
            RiskIndexRoles.SeededGrants.Keys.Order());
        Assert.DoesNotContain(RiskIndexRoles.Administrator, RiskIndexRoles.SeededGrants.Keys);
    }

    [Fact]
    public void TestTheApproverRolesActForTheCommittee()
    {
        var expected = new[]
        {
            EntityPermissions.RiskContext, RiskIndexPermissions.Approve, RiskIndexPermissions.Configure, RiskIndexPermissions.View
        };

        Assert.Equal(expected, RiskIndexRoles.SeededGrants[RiskIndexRoles.RiskManager].Order());
        Assert.Equal(expected, RiskIndexRoles.SeededGrants[RiskIndexRoles.RiskAdministrator].Order());
    }

    [Fact]
    public void TestTheAnalystAuthorsButNeverApproves()
    {
        Assert.Equal(new[] { RiskIndexPermissions.Configure, RiskIndexPermissions.View },
            RiskIndexRoles.SeededGrants[RiskIndexRoles.RiskAnalyst].Order());
    }

    [Fact]
    public void TestTheDefaultApproverRolesAreTheTwoSeededApproverRoles()
    {
        Assert.Equal(new[] { RiskIndexRoles.RiskManager, RiskIndexRoles.RiskAdministrator },
            RiskIndexRoles.DefaultApproverRoles.Split(','));
        Assert.All(RiskIndexRoles.DefaultApproverRoles.Split(','),
            role => Assert.Contains(RiskIndexPermissions.Approve, RiskIndexRoles.SeededGrants[role]));
    }
}
