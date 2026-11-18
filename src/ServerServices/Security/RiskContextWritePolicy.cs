using DAL.Context;
using Model.Entities;
using Model.Exceptions;

namespace ServerServices.Security;

/// <summary>
/// Who may set or change a risk-context property (S40 §6.7 item 5, T241): <c>entity_risk_context</c> (or Admin) and
/// unrestricted scope. The one place this rule lives — the entity form's write guard and the risk-context batch both call
/// it — so T295's subtree rule changes only this body. <c>RiskContextWritePolicyTest</c> pins it.
/// </summary>
public static class RiskContextWritePolicy
{
    /// <summary>Throws the refusal, if any. <paramref name="entityIds"/>: the entities changed (on create, the parent).</summary>
    /// <param name="holdsRiskContextPermission">The caller holds <c>entity_risk_context</c> or the Admin role.</param>
    /// <param name="scope">The caller's entity scope.</param>
    /// <param name="entityIds">Unused today; the input T295's "the caller holds the entity's subtree" rule reads.</param>
    /// <param name="what">The change, for the message ("criticality", "the risk context").</param>
    /// <exception cref="EntityWriteRefusedException">403 <c>risk_context_requires_permission</c> or <c>…_global_scope</c>.</exception>
    public static void Enforce(bool holdsRiskContextPermission, EntityScope scope, IReadOnlyCollection<int> entityIds,
        string what)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(entityIds);

        if (!holdsRiskContextPermission)
            throw new EntityWriteRefusedException(EntityWriteErrors.RiskContextRequiresPermission,
                $"Changing {what} requires the {EntityPermissions.RiskContext} permission.");

        if (!scope.IsUnrestricted)
            throw new EntityWriteRefusedException(EntityWriteErrors.RiskContextRequiresGlobalScope,
                $"Changing {what} requires global scope: a process's criticality moves every unit it serves.");
    }
}
