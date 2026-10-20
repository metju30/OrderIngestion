using System.Text.Json;
using Microsoft.Extensions.Logging;
using OrderIngestion.Application.Interfaces;
using OrderIngestion.Domain.Entities;

namespace OrderIngestion.Infrastructure.Sinks;

/// <summary>
/// Appends orders as one JSON object per line. Cheap, crash-safe (append only),
/// and easily readable by any downstream tool — behaves like a tiny message queue.
/// </summary>
public class FileOrderSink : IOrderSink
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private readonly string _queuePath;
    private readonly FileQueueLock _lock;
    private readonly ILogger<FileOrderSink> _logger;

    public FileOrderSink(string queuePath, FileQueueLock gate, ILogger<FileOrderSink> logger)
    {
        _queuePath = queuePath;
        _lock = gate;
        _logger = logger;
    }

    public async Task<(int Inserted, int Skipped)> AcceptAsync(IReadOnlyList<Order> orders, CancellationToken ct = default)
    {
        if (orders.Count == 0) return (0, 0);

        await _lock.Semaphore.WaitAsync(ct);
        try
        {
            var dir = Path.GetDirectoryName(_queuePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            await using var stream = new FileStream(
                _queuePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            await using var writer = new StreamWriter(stream);

            foreach (var order in orders)
                await writer.WriteLineAsync(JsonSerializer.Serialize(order, JsonOpts));

            await writer.FlushAsync(ct);
            _logger.LogDebug("Appended {Count} orders to {Path}", orders.Count, _queuePath);
            return (orders.Count, 0);
        }
        finally { _lock.Semaphore.Release(); }
    }
}