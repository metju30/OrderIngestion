using OrderIngestion.Application.Interfaces;
using OrderIngestion.Domain.Entities;

namespace OrderIngestion.Infrastructure.Sinks;

public class SqliteOrderSink : IOrderSink
{
    private readonly IOrderRepository _repository;
    public SqliteOrderSink(IOrderRepository repository) => _repository = repository;

    public async Task<(int Inserted, int Skipped)> AcceptAsync(IReadOnlyList<Order> orders, CancellationToken ct = default)
    {
        var result = await _repository.AddRangeIfNotExistsWithCountAsync(orders, ct);
        return result;
    }
}