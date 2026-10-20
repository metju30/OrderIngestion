using OrderIngestion.Application.Interfaces;
using OrderIngestion.Application.Models;
using OrderIngestion.Domain.Entities;

namespace OrderIngestion.Infrastructure.Tests.Fakes;

public sealed class FakeOrderSource : IOrderSource
{
    private readonly Func<Task<IReadOnlyList<Order>>> _fetch;
    public FakeOrderSource(Func<Task<IReadOnlyList<Order>>> fetch) => _fetch = fetch;

    public static FakeOrderSource Returns(params Order[] orders)
        => new(() => Task.FromResult<IReadOnlyList<Order>>(orders));

    public static FakeOrderSource Throws(Exception ex)
        => new(() => Task.FromException<IReadOnlyList<Order>>(ex));

    public Task<IReadOnlyList<Order>> FetchOrdersAsync(CancellationToken ct = default) => _fetch();
}

public sealed class FakeOrderSink : IOrderSink
{
    public List<IReadOnlyList<Order>> Batches { get; } = new();
    public (int Inserted, int Skipped) NextResult { get; set; } = (0, 0);

    public Task<(int Inserted, int Skipped)> AcceptAsync(IReadOnlyList<Order> orders, CancellationToken ct = default)
    {
        Batches.Add(orders);
        return Task.FromResult(NextResult);
    }
}

public sealed class FakeIngestionLogRepository : IIngestionLogRepository
{
    public sealed record Completion(
        int LogId, IngestionStatus Status,
        int Fetched, int Inserted, int Skipped, int Rejected, int Retry, string? ErrorMessage);

    public List<Completion> Completions { get; } = new();

    public Task<int> StartExecutionAsync(string runId, DateTime startedAtUtc, IngestionRunKind kind, CancellationToken ct = default)
        => Task.FromResult(1);

    public Task CompleteExecutionAsync(
        int logId, DateTime finishedAtUtc, IngestionStatus status,
        int fetched, int inserted, int skipped, int rejected, int retry,
        string? errorMessage, CancellationToken ct = default)
    {
        Completions.Add(new Completion(logId, status, fetched, inserted, skipped, rejected, retry, errorMessage));
        return Task.CompletedTask;
    }
}

public sealed class FakeRunMetrics : IRunMetrics
{
    public int RetryCount => RunMetricsContext.RetryCount;
    public void IncrementRetry() => RunMetricsContext.IncrementRetry();
}