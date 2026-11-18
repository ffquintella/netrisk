using DAL.Context;
using JetBrains.Annotations;
using Model.Entities;
using Model.Exceptions;
using ServerServices.Security;
using Xunit;

namespace ServerServices.Tests.Security;

/// <summary>
/// S40 §6.7 item 5 (T241, T264): the one rule both the entity form's guard and the risk-context batch call — the
/// permission, then unrestricted scope. A scoped holder is refused even for an entity inside their subtree (T295 open).
/// </summary>
[TestSubject(typeof(RiskContextWritePolicy))]
public class RiskContextWritePolicyTest
{
    [Fact]
    public void TestWithoutThePermissionTheChangeIsRefusedWhateverTheScope()
    {
        var ex = Assert.Throws<EntityWriteRefusedException>(() =>
            RiskContextWritePolicy.Enforce(false, EntityScope.Unrestricted, [12], "criticality"));

        Assert.Equal((EntityWriteErrors.RiskContextRequiresPermission, 403), (ex.Error, ex.StatusCode));
        Assert.Contains(EntityPermissions.RiskContext, ex.Message);
    }

    [Fact]
    public void TestAScopedHolderIsRefusedEvenForAnEntityInTheirSubtree()
    {
        var ex = Assert.Throws<EntityWriteRefusedException>(() =>
            RiskContextWritePolicy.Enforce(true, EntityScope.ForEntities([10, 12]), [12], "criticality"));

        Assert.Equal((EntityWriteErrors.RiskContextRequiresGlobalScope, 403), (ex.Error, ex.StatusCode));
    }

    [Fact]
    public void TestTheHolderWithGlobalScopeMayChangeTheContext()
    {
        RiskContextWritePolicy.Enforce(true, EntityScope.Unrestricted, [12], "the risk context");
    }
}
