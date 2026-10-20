using OrderIngestion.Domain.Entities;

namespace OrderIngestion.Application.Interfaces;

public interface IOrderRepository
{
    /// <summary>Returns (inserted, skipped) counts where skipped = duplicates filtered by the sink.</summary>
    Task<(int Inserted, int Skipped)> AddRangeIfNotExistsWithCountAsync(IEnumerable<Order> orders, CancellationToken ct = default);
}