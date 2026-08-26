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
