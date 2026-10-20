using Microsoft.EntityFrameworkCore;
using OrderIngestion.Application.Interfaces;
using OrderIngestion.Application.Models;

namespace OrderIngestion.Infrastructure.Persistence;

public class SqliteIngestionLogRepository : IIngestionLogRepository
{
    private readonly OrderDbContext _db;
    public SqliteIngestionLogRepository(OrderDbContext db) => _db = db;

    public async Task<int> StartExecutionAsync(
        string runId, DateTime startedAtUtc, IngestionRunKind kind, CancellationToken ct = default)
    {
        var entry = new IngestionLog
        {
            RunId = runId,
            StartedAtUtc = startedAtUtc,
            RunKind = kind,
            FinishedAtUtc = null,
            Status = IngestionStatus.Running
            // all counters default to 0
        };
        _db.IngestionLogs.Add(entry);
        await _db.SaveChangesAsync(ct);
        return entry.Id;
    }

    public async Task CompleteExecutionAsync(
        int logId, DateTime finishedAtUtc, IngestionStatus status,
        int fetched, int inserted, int skipped, int rejected, int retry,
        string? errorMessage, CancellationToken ct = default)
    {
        var entry = await _db.IngestionLogs.FindAsync(new object[] { logId }, ct);
        if (entry is null) return;   // row was deleted externally; nothing to update

        entry.FinishedAtUtc = finishedAtUtc;
        entry.Status = status;
        entry.FetchedCount = fetched;
        entry.InsertedCount = inserted;
        entry.SkippedCount = skipped;
        entry.RejectedCount = rejected;
        entry.RetryCount = retry;
        entry.ErrorMessage = errorMessage;

        await _db.SaveChangesAsync(ct);
    }
}