using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace ServerServices.Governance;

/// <summary>
/// Track 8 milestone 8.6.2 — reviewer appointments.
///
/// Two invariants. A user is appointed to an entity at most once, which the unique index enforces
/// and this service turns into an idempotent update rather than a constraint violation. And at most
/// one reviewer per entity is primary, which nothing but this service can enforce — appointing a new
/// primary demotes the old one in the same save, because "who gets chased when the campaign is
/// overdue" must not be a judgement call.
/// </summary>
public class EntityRiskReviewersService(ILogger logger, IDalService dalService)
    : ServiceBase(logger, dalService), IEntityRiskReviewersService
{
    public async Task<List<EntityRiskReviewer>> GetByEntityAsync(int entityId)
    {
        await using var db = DalService.GetContext();

        return await db.EntityRiskReviewers
            .Where(r => r.EntityId == entityId)
            .Include(r => r.User)
            .Include(r => r.AppointedBy)
            .OrderByDescending(r => r.IsPrimary)
            .ThenBy(r => r.Id)
            .ToListAsync();
    }

    public async Task<List<int>> GetEntitiesForReviewerAsync(int userId)
    {
        await using var db = DalService.GetContext();

        return await db.EntityRiskReviewers
            .Where(r => r.UserId == userId)
            .Select(r => r.EntityId)
            .Distinct()
            .ToListAsync();
    }

    public async Task<EntityRiskReviewer> AppointAsync(int entityId, int userId, bool isPrimary,
        int actingUserId)
    {
        await using var db = DalService.GetContext();

        if (!await db.Entities.AnyAsync(e => e.Id == entityId))
            throw new DataNotFoundException("local", "entities",
                new Exception($"Entity with id {entityId} not found"));

        var user = await db.Users.FirstOrDefaultAsync(u => u.Value == userId)
                   ?? throw new DataNotFoundException("local", "user",
                       new Exception($"User with id {userId} not found"));

        if (user.Enabled != true)
            throw new InvalidParameterException(nameof(userId),
                "A disabled account cannot be appointed a risk reviewer — the campaigns would be " +
                "assigned to somebody who cannot sign in to answer them.");

        var existing = await db.EntityRiskReviewers
            .FirstOrDefaultAsync(r => r.EntityId == entityId && r.UserId == userId);

        if (isPrimary)
        {
            // Demote whoever holds it. Done in the same save so there is never a moment with two
            // primaries or none.
            var incumbents = await db.EntityRiskReviewers
                .Where(r => r.EntityId == entityId && r.IsPrimary && r.UserId != userId)
                .ToListAsync();

            foreach (var incumbent in incumbents) incumbent.IsPrimary = false;
        }

        // The appointment on its own grants nothing. Track 2.3 scopes every read by the caller's
        // `user_entity_roles` assignments, and a non-admin with none of them sees an empty register —
        // so an administrator who appointed a reviewer and stopped there would watch the portal show
        // that reviewer no campaigns at all, with nothing in the schema to explain why. Found by
        // standing the stack up and signing in as a freshly appointed reviewer.
        await EnsureEntityAssignmentAsync(db, entityId, userId);

        if (existing != null)
        {
            existing.IsPrimary = isPrimary;
            await db.SaveChangesAsync();
            return existing;
        }

        var appointment = new EntityRiskReviewer
        {
            EntityId = entityId,
            UserId = userId,
            IsPrimary = isPrimary,
            AppointedById = actingUserId,
            CreatedAt = DateTime.UtcNow
        };

        db.EntityRiskReviewers.Add(appointment);
        await db.SaveChangesAsync();

        Logger.Information("User {User} appointed risk reviewer for entity {Entity} by {Actor}", userId,
            entityId, actingUserId);

        return appointment;
    }

    /// <summary>
    /// Gives the reviewer a Track 2.3 assignment to the entity if they do not already have a live
    /// one, so the appointment actually lets them see the entity's risks.
    ///
    /// The role is the user's own — the assignment carries scope, not privilege, and inventing a
    /// stronger role here would turn "you may review this entity" into "you may do whatever that role
    /// permits, on this entity". A user with no role at all takes the lowest-numbered configured one,
    /// because <c>user_entity_roles.role_id</c> is a required foreign key.
    /// </summary>
    private static async Task EnsureEntityAssignmentAsync(DAL.Context.AuditableContext db, int entityId,
        int userId)
    {
        var alreadyAssigned = await db.UserEntityRoles
            .AnyAsync(a => a.UserId == userId && a.EntityId == entityId && a.RevokedAt == null);

        if (alreadyAssigned) return;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Value == userId);
        if (user is null) return;

        var roleId = user.RoleId > 0
            ? user.RoleId
            : await db.Roles.OrderBy(r => r.Value).Select(r => r.Value).FirstOrDefaultAsync();

        if (roleId <= 0) return;

        db.UserEntityRoles.Add(new UserEntityRole
        {
            UserId = userId,
            EntityId = entityId,
            RoleId = roleId,
            CreatedAt = DateTime.UtcNow
        });
    }

    public async Task RemoveAsync(int id)
    {
        await using var db = DalService.GetContext();

        var appointment = await db.EntityRiskReviewers.FirstOrDefaultAsync(r => r.Id == id)
                          ?? throw new DataNotFoundException("local", "entity_risk_reviewers",
                              new Exception($"Reviewer appointment with id {id} not found"));

        db.EntityRiskReviewers.Remove(appointment);
        await db.SaveChangesAsync();

        Logger.Information("Risk-reviewer appointment {Id} removed (user {User}, entity {Entity})", id,
            appointment.UserId, appointment.EntityId);
    }
}
