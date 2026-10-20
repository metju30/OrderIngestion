using OrderIngestion.Application.Interfaces;

namespace OrderIngestion.Infrastructure.Importers;

/// <summary>Used in SQLite mode — the sink already wrote to the final store.</summary>
public class NoOpOrderImporter : IOrderImporter
{
    public Task<int> ImportPendingAsync(CancellationToken ct = default) => Task.FromResult(0);
}