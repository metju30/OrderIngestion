using OrderIngestion.Domain.Entities;

namespace OrderIngestion.Application.Interfaces;

public interface IOrderSource
{
    Task<IReadOnlyList<Order>> FetchOrdersAsync(CancellationToken cancellationToken = default);
}