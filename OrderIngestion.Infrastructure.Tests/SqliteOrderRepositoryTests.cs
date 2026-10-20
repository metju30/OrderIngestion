using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OrderIngestion.Application.Models;
using OrderIngestion.Domain.Entities;
using OrderIngestion.Infrastructure.Persistence;

namespace OrderIngestion.Infrastructure.Tests;

public class SqliteOrderRepositoryTests : IAsyncLifetime
{
    private SqliteConnection _conn = null!;
    private OrderDbContext _db = null!;
    private SqliteOrderRepository _repo = null!;
    private SqliteIngestionLogRepository _logRepo = null!;

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("Filename=:memory:");
        await _conn.OpenAsync();
        var opts = new DbContextOptionsBuilder<OrderDbContext>().UseSqlite(_conn).Options;
        _db = new OrderDbContext(opts);
        await _db.Database.EnsureCreatedAsync(); // They test the sink logic and implicitly require the unique index to exist, since EnsureCreated() builds the real schema.
        _repo = new SqliteOrderRepository(_db);
        _logRepo = new SqliteIngestionLogRepository(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _conn.DisposeAsync();
    }

    private static Order Make(int id) => new() { ExternalId = id, UserId = 1, Title = "T", Body = "B" };

    [Fact]
    public async Task AddRangeIfNotExistsWithCountAsync_NewOrder_InsertsOnce()
    {
        var (inserted, skipped) = await _repo.AddRangeIfNotExistsWithCountAsync(new[] { Make(1) });

        Assert.Equal(1, inserted);
        Assert.Equal(0, skipped);
        Assert.Equal(1, await _db.Orders.CountAsync());
    }

    [Fact]
    public async Task AddRangeIfNotExistsWithCountAsync_SameOrderTwice_OnlyOneRow()
    {
        await _repo.AddRangeIfNotExistsWithCountAsync(new[] { Make(1) });

        var (inserted, skipped) = await _repo.AddRangeIfNotExistsWithCountAsync(new[] { Make(1) });

        Assert.Equal(0, inserted);
        Assert.Equal(1, skipped);
        Assert.Equal(1, await _db.Orders.CountAsync());
    }

    [Fact]
    public async Task AddRangeIfNotExistsWithCountAsync_MixedBatch_InsertsOnlyNew()
    {
        await _repo.AddRangeIfNotExistsWithCountAsync(new[] { Make(1) });

        var (inserted, skipped) = await _repo.AddRangeIfNotExistsWithCountAsync(
            new[] { Make(1), Make(2), Make(3) });

        Assert.Equal(2, inserted);
        Assert.Equal(1, skipped);
        Assert.Equal(3, await _db.Orders.CountAsync());
    }

    [Fact]
    public async Task AddRangeIfNotExistsWithCountAsync_OlderUnseenId_IsInserted()
    {
        // Seed a high ID first, simulating "time has passed and we've already seen 50".
        await _repo.AddRangeIfNotExistsWithCountAsync(new[] { Make(50) });

        // Now an older-but-unseen ID (10) arrives alongside a duplicate (50).
        var (inserted, skipped) = await _repo.AddRangeIfNotExistsWithCountAsync(
            new[] { Make(10), Make(50) });

        Assert.Equal(1, inserted);
        Assert.Equal(1, skipped);

        var ids = await _db.Orders.Select(o => o.ExternalId).OrderBy(x => x).ToListAsync();
        Assert.Equal(new[] { 10, 50 }, ids);
    }

    [Fact]
    public async Task AddRangeIfNotExistsWithCountAsync_DuplicateIdWithinPayload_DoesNotFailBatch()
    {
        var (inserted, skipped) = await _repo.AddRangeIfNotExistsWithCountAsync(
            new[] { Make(1), Make(1), Make(2) });

        Assert.Equal(2, inserted);
        Assert.Equal(1, skipped);
        Assert.Equal(2, await _db.Orders.CountAsync());
    }

    [Fact]
    public async Task StartExecutionAsync_WithImportKind_PersistsRunKind()
    {
        var startedAt = DateTime.UtcNow;

        var logId = await _logRepo.StartExecutionAsync(
            "run-import-1", startedAt, IngestionRunKind.Import);

        // Re-query via a fresh context view to prove the value round-trips through the DB,
        // not just that the entity in memory was modified.
        _db.ChangeTracker.Clear();
        var row = await _db.IngestionLogs.SingleAsync(l => l.Id == logId);

        Assert.Equal("run-import-1", row.RunId);
        Assert.Equal(IngestionRunKind.Import, row.RunKind);
        Assert.Equal(IngestionStatus.Running, row.Status);
        Assert.Null(row.FinishedAtUtc);
    }

    [Fact]
    public async Task StartExecutionAsync_WithIngestionKind_PersistsRunKind()
    {
        var startedAt = DateTime.UtcNow;

        var logId = await _logRepo.StartExecutionAsync(
            "run-ingest-1", startedAt, IngestionRunKind.Ingestion);

        _db.ChangeTracker.Clear();
        var row = await _db.IngestionLogs.SingleAsync(l => l.Id == logId);

        Assert.Equal(IngestionRunKind.Ingestion, row.RunKind);
    }
}