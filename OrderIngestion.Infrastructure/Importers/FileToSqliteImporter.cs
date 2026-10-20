using Microsoft.Extensions.Logging;
using OrderIngestion.Application.Interfaces;
using OrderIngestion.Application.Models;
using OrderIngestion.Domain.Entities;
using OrderIngestion.Infrastructure.Sinks;
using System.Text.Json;

namespace OrderIngestion.Infrastructure.Importers;

/// <summary>
/// Drains the JSON-lines queue into SQLite.
/// Rename-then-process ensures no partial batch is lost: the queue file is atomically
/// moved to a unique ".processing" name, read to completion, inserted, then deleted.
/// A crash mid-import leaves the *.processing file behind, which is picked up next run.
/// </summary>
public class FileToSqliteImporter : IOrderImporter
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private readonly string _queuePath;
    private readonly IOrderRepository _repository;
    private readonly IIngestionLogRepository _logRepository;
    private readonly FileQueueLock _lock;
    private readonly ILogger<FileToSqliteImporter> _logger;

    public FileToSqliteImporter(
        string queuePath,
        IOrderRepository repository,
        IIngestionLogRepository logRepository,
        FileQueueLock gate,
        ILogger<FileToSqliteImporter> logger)
    {
        _queuePath = queuePath;
        _repository = repository;
        _logRepository = logRepository;
        _lock = gate;
        _logger = logger;
    }

    private bool HasPendingWork()
    {
        if (File.Exists(_queuePath)) return true;

        var dir = Path.GetDirectoryName(_queuePath);
        var name = Path.GetFileName(_queuePath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;

        return Directory.EnumerateFiles(dir, name + ".*.processing").Any();
    }

    public async Task<int> ImportPendingAsync(CancellationToken ct = default)
    {
        if (!HasPendingWork())
            return 0; // nothing to do — do not create a log row

        var runId = Guid.NewGuid().ToString("N");
        var startedAt = DateTime.UtcNow;
        int fetched = 0, inserted = 0, skipped = 0, rejected = 0;
        string? error = null;
        var status = IngestionStatus.Running;

        var logId = await _logRepository.StartExecutionAsync(runId, startedAt, IngestionRunKind.Import, ct);

        using (_logger.BeginScope(new Dictionary<string, object> { ["RunId"] = runId }))
        {
            RunMetricsContext.StartRun();
            try
            {
                // 1. Drain any leftovers first (from a previous crash).
                var r1 = await ProcessLeftoversAsync(ct);
                fetched += r1.Fetched; inserted += r1.Inserted; skipped += r1.Skipped; rejected += r1.Rejected;

                // 2. Atomically move the live queue file aside under the shared lock.
                string? processingPath = null;
                await _lock.Semaphore.WaitAsync(ct);
                try
                {
                    if (File.Exists(_queuePath))
                    {
                        processingPath = $"{_queuePath}.{Guid.NewGuid():N}.processing";
                        File.Move(_queuePath, processingPath);
                    }
                }
                finally { _lock.Semaphore.Release(); }

                // 3. Process the drained batch (writes may now continue into a fresh queue file).
                if (processingPath is not null)
                {
                    var r2 = await ProcessFileAsync(processingPath, ct);
                    fetched += r2.Fetched; inserted += r2.Inserted; skipped += r2.Skipped; rejected += r2.Rejected;
                }

                status = rejected == 0 ? IngestionStatus.Succeeded : IngestionStatus.PartiallySucceeded;
                return inserted;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                status = IngestionStatus.Failed;
                throw;
            }
            finally
            {
                try
                {
                    await _logRepository.CompleteExecutionAsync(
                        logId, DateTime.UtcNow, status,
                        fetched, inserted, skipped, rejected, RunMetricsContext.RetryCount, error, ct);
                }
                catch (Exception logEx)
                {
                    _logger.LogError(logEx, "Failed to persist IngestionLogs row for importer run {RunId}", runId);
                    if (status != IngestionStatus.Failed)
                    {
                        throw;
                    }
                }
                finally
                {
                    RunMetricsContext.EndRun();
                }
            }
        }
    }

    private async Task<(int Fetched, int Inserted, int Skipped, int Rejected)> ProcessLeftoversAsync(CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_queuePath);
        var name = Path.GetFileName(_queuePath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return (0, 0, 0, 0);

        int f = 0, i = 0, s = 0, r = 0;
        foreach (var leftover in Directory.EnumerateFiles(dir, name + ".*.processing"))
        {
            var part = await ProcessFileAsync(leftover, ct);
            f += part.Fetched; i += part.Inserted; s += part.Skipped; r += part.Rejected;
        }
        return (f, i, s, r);
    }

    private async Task<(int Fetched, int Inserted, int Skipped, int Rejected)> ProcessFileAsync(string path, CancellationToken ct)
    {
        var orders = new List<Order>();
        int rejected = 0;

        await foreach (var line in File.ReadLinesAsync(path, ct))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var order = JsonSerializer.Deserialize<Order>(line, JsonOpts);
                if (order is not null) orders.Add(order);
                else rejected++;
            }
            catch (JsonException ex)
            {
                rejected++;
                _logger.LogWarning(ex, "Skipping malformed line in {File}", path);
            }
        }

        var fetched = orders.Count + rejected;

        if (orders.Count == 0)
        {
            File.Delete(path);
            return (fetched, 0, 0, rejected);
        }

        var (inserted, skipped) = await _repository.AddRangeIfNotExistsWithCountAsync(orders, ct);
        File.Delete(path);

        _logger.LogInformation(
            "Imported {Inserted}/{Fetched} orders from {File} into SQLite (skipped {Skipped} duplicates, rejected {Rejected} malformed)",
            inserted, fetched, path, skipped, rejected);

        return (fetched, inserted, skipped, rejected);
    }
}