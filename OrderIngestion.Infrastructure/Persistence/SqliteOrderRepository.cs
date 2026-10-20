using Microsoft.EntityFrameworkCore;
using OrderIngestion.Application.Interfaces;
using OrderIngestion.Domain.Entities;
using SQLitePCL;

namespace OrderIngestion.Infrastructure.Persistence;

public class SqliteOrderRepository : IOrderRepository
{
    private readonly OrderDbContext _db;
    public SqliteOrderRepository(OrderDbContext db) => _db = db;

    /// <summary>Returns (inserted, skipped) counts where skipped = duplicates filtered by the sink.</summary>
    public async Task<(int Inserted, int Skipped)> AddRangeIfNotExistsWithCountAsync(
        IEnumerable<Order> orders, CancellationToken ct = default)
    {
        var raw = orders.ToList();
        if (raw.Count == 0) return (0, 0);

        // Defensive: the source contract says ExternalIds are unique within a payload,
        // but a violation would otherwise throw DbUpdateException and fail the whole batch.
        var incoming = raw.DistinctBy(o => o.ExternalId).ToList();
        var inBatchDuplicates = raw.Count - incoming.Count;

        var externalIds = incoming.Select(o => o.ExternalId).ToList();
        var existing = await _db.Orders
            .Where(o => externalIds.Contains(o.ExternalId))
            .Select(o => o.ExternalId)
            .ToListAsync(ct);
        var existingSet = existing.ToHashSet();

        var newOnes = incoming.Where(o => !existingSet.Contains(o.ExternalId)).ToList();
        // Skipped = two mutually exclusive categories:
        //   (a) ExternalId already exists in the database            -> (incoming.Count - newOnes.Count)
        //   (b) ExternalId appeared more than once within this batch -> inBatchDuplicates
        var skipped = (incoming.Count - newOnes.Count) + inBatchDuplicates;

        if (newOnes.Count == 0) return (0, skipped);

        await _db.Orders.AddRangeAsync(newOnes, ct);
        var inserted = await _db.SaveChangesAsync(ct);
        return (inserted, skipped);
    }
}