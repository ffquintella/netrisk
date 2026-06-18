using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebSiteData.Entities;

namespace WebSiteData.Services;

public class OutboxService : IOutboxService
{
    private readonly IDbContextFactory<WebSiteDbContext> _factory;

    public OutboxService(IDbContextFactory<WebSiteDbContext> factory)
    {
        _factory = factory;
    }

    public async Task EnqueueAsync(string actionType, object payload, DateTime? actionTimeUtc = null)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.Outbox.Add(new OutboxItem
        {
            ClientActionId = Guid.NewGuid(),
            ActionType = actionType,
            PayloadJson = JsonSerializer.Serialize(payload),
            CreatedAt = DateTime.UtcNow,
            ActionTimeUtc = actionTimeUtc ?? DateTime.UtcNow,
            Status = OutboxStatus.Pending
        });
        await db.SaveChangesAsync();
    }

    public async Task<List<OutboxItem>> GetPendingAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Outbox
            .Where(x => x.Status == OutboxStatus.Pending || x.Status == OutboxStatus.Sent)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();
    }

    public async Task MarkSentAsync(IEnumerable<Guid> ids)
    {
        var set = ids.ToHashSet();
        await using var db = await _factory.CreateDbContextAsync();
        var items = await db.Outbox.Where(x => set.Contains(x.ClientActionId)).ToListAsync();
        foreach (var item in items)
        {
            if (item.Status == OutboxStatus.Pending) item.Status = OutboxStatus.Sent;
            item.Attempts++;
        }
        await db.SaveChangesAsync();
    }

    public async Task MarkAckedAsync(IEnumerable<Guid> ids)
    {
        var set = ids.ToHashSet();
        await using var db = await _factory.CreateDbContextAsync();
        var items = await db.Outbox.Where(x => set.Contains(x.ClientActionId)).ToListAsync();
        // Acked actions are durable on the server now; remove them to keep the outbox small.
        db.Outbox.RemoveRange(items);
        await db.SaveChangesAsync();
    }
}
