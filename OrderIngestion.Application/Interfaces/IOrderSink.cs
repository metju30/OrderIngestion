using OrderIngestion.Domain.Entities;

namespace OrderIngestion.Application.Interfaces;

/// <summary>
/// Where fetched orders land first. Either the final storage (SQLite) or an intermediate queue (file), depending on configuration.
/// </summary>
public interface IOrderSink
{
    /// <summary>Accepts orders. Returns (Inserted, Skipped) counts.</summary>
    Task<(int Inserted, int Skipped)> AcceptAsync(IReadOnlyList<Order> orders, CancellationToken ct = default);
}