using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace ServerServices.Governance;

/// <summary>
/// Track 8 milestone 8.3.3 — CRUD over the appetite thresholds.
///
/// The invariants are small and both matter. There is at most one organization-wide row, which the
/// unique index cannot enforce because MySQL treats every NULL as distinct. And the dual-approval
/// threshold cannot exceed the ceiling: a threshold above the ceiling can never fire, and a
/// configuration that can never fire reads to everyone who sees it as a control that is in place.
/// </summary>
public class RiskAppetitesService(ILogger logger, IDalService dalService)
    : ServiceBase(logger, dalService), IRiskAppetitesService
{
    public async Task<List<RiskAppetite>> GetAllAsync()
    {
        await using var db = DalService.GetContext();

        return await db.RiskAppetites
            .Include(a => a.Entity)
            .OrderBy(a => a.EntityId == null ? 0 : 1)
            .ThenBy(a => a.EntityId)
            .ToListAsync();
    }

    public async Task<RiskAppetite?> GetGlobalAsync()
    {
        await using var db = DalService.GetContext();
        return await db.RiskAppetites.FirstOrDefaultAsync(a => a.EntityId == null);
    }

    public async Task<RiskAppetite> SaveAsync(RiskAppetite appetite, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(appetite);

        if (appetite.MaxAcceptableResidual < 0)
            throw new InvalidParameterException(nameof(appetite.MaxAcceptableResidual),
                "A negative ceiling would refuse every acceptance, including of risks scored zero.");

        if (appetite.DualApprovalThreshold < 0)
            throw new InvalidParameterException(nameof(appetite.DualApprovalThreshold),
                "A negative dual-approval threshold would escalate everything.");

        if (appetite.DualApprovalThreshold > appetite.MaxAcceptableResidual)
            throw new InvalidParameterException(nameof(appetite.DualApprovalThreshold),
                "The dual-approval threshold has to be at or below the acceptance ceiling. Above it the " +
                "threshold can never fire — anything that would trigger it is already refused — and a " +
                "setting that can never fire still reads as a control that is in place.");

        await using var db = DalService.GetContext();

        if (appetite.EntityId is not null &&
            !await db.Entities.AnyAsync(e => e.Id == appetite.EntityId.Value))
            throw new DataNotFoundException("local", "entities",
                new Exception($"Entity with id {appetite.EntityId} not found"));

        var existing = appetite.Id > 0
            ? await db.RiskAppetites.FirstOrDefaultAsync(a => a.Id == appetite.Id)
            : await db.RiskAppetites.FirstOrDefaultAsync(a => a.EntityId == appetite.EntityId);

        if (existing is null)
        {
            // The single-global rule, enforced here because the unique index cannot: MySQL treats
            // every NULL as distinct, so two global rows would insert happily and then disagree.
            if (appetite.EntityId is null && await db.RiskAppetites.AnyAsync(a => a.EntityId == null))
                throw new DataAlreadyExistsException("local", "risk_appetites", "global",
                    "An organization-wide appetite already exists. Edit it rather than adding a second — " +
                    "two global appetites would mean the gate depends on which row is read first.");

            var created = new RiskAppetite
            {
                EntityId = appetite.EntityId,
                MaxAcceptableResidual = appetite.MaxAcceptableResidual,
                DualApprovalThreshold = appetite.DualApprovalThreshold,
                Notes = appetite.Notes,
                CreatedAt = DateTime.UtcNow,
                CreatedById = actingUserId
            };

            db.RiskAppetites.Add(created);
            await db.SaveChangesAsync();

            Logger.Information(
                "Risk appetite created for entity {Entity}: ceiling {Ceiling}, dual approval above {Dual}",
                appetite.EntityId?.ToString() ?? "(global)", created.MaxAcceptableResidual,
                created.DualApprovalThreshold);

            return created;
        }

        var oldCeiling = existing.MaxAcceptableResidual;

        existing.MaxAcceptableResidual = appetite.MaxAcceptableResidual;
        existing.DualApprovalThreshold = appetite.DualApprovalThreshold;
        existing.Notes = appetite.Notes;
        existing.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        // Raising the ceiling is how an organization makes a previously unacceptable risk
        // acceptable, which is exactly the act the audit trail exists to capture. The interceptor
        // records the field change; this line makes it visible in the operational log too.
        if (appetite.MaxAcceptableResidual > oldCeiling)
            Logger.Warning(
                "Risk appetite ceiling for entity {Entity} RAISED from {Old} to {New} by user {User}",
                existing.EntityId?.ToString() ?? "(global)", oldCeiling, existing.MaxAcceptableResidual,
                actingUserId);

        return existing;
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = DalService.GetContext();

        var appetite = await db.RiskAppetites.FirstOrDefaultAsync(a => a.Id == id)
                       ?? throw new DataNotFoundException("local", "risk_appetites",
                           new Exception($"Risk appetite with id {id} not found"));

        db.RiskAppetites.Remove(appetite);
        await db.SaveChangesAsync();

        Logger.Warning("Risk appetite {Id} for entity {Entity} deleted; that scope now falls back to the " +
                       "organization-wide appetite, or to no gate at all if there is none", id,
            appetite.EntityId?.ToString() ?? "(global)");
    }
}
