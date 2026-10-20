using Microsoft.Extensions.Logging;
using OrderIngestion.Application.Interfaces;
using OrderIngestion.Application.Models;
using OrderIngestion.Domain.Entities;
using OrderIngestion.Application.Validation;

namespace OrderIngestion.Application.Services;

public class OrderIngestionService
{
    private readonly IOrderSource _source;
    private readonly IOrderSink _sink;
    private readonly IIngestionLogRepository _logRepository;
    private readonly IRunMetrics _metrics;
    private readonly ILogger<OrderIngestionService> _logger;

    public OrderIngestionService(
        IOrderSource source,
        IOrderSink sink,
        IIngestionLogRepository logRepository,
        IRunMetrics metrics,
        ILogger<OrderIngestionService> logger)
    {
        _source = source;
        _sink = sink;
        _logRepository = logRepository;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var runId = Guid.NewGuid().ToString("N");
        var startedAt = DateTime.UtcNow;
        int fetched = 0, inserted = 0, skipped = 0, rejected = 0;
        string? errorMessage = null;
        var status = IngestionStatus.Running;

        var logId = await _logRepository.StartExecutionAsync(runId, startedAt, IngestionRunKind.Ingestion, ct);

        using (_logger.BeginScope(new Dictionary<string, object> { ["RunId"] = runId }))
        {
            RunMetricsContext.StartRun();
            try
            {
                _logger.LogInformation("Starting order ingestion at {Time:u} (UTC)", startedAt);

                var allOrders = await _source.FetchOrdersAsync(ct);
                fetched = allOrders.Count;

                var validOrders = new List<Order>();
                foreach (var order in allOrders)
                {
                    if (!OrderValidator.IsValid(order, out var reason))
                    {
                        rejected++;
                        _logger.LogWarning("Rejecting order with ExternalId {ExternalId}: {Reason}", order.ExternalId, reason);
                    }
                    else
                    {
                        validOrders.Add(order);
                    }
                }

                if (validOrders.Count > 0)
                {
                    var result = await _sink.AcceptAsync(validOrders, ct);
                    inserted = result.Inserted;
                    skipped = result.Skipped;
                    _logger.LogInformation("Accepted {Inserted} new orders, skipped {Skipped} duplicates", inserted, skipped);
                }
                else
                {
                    _logger.LogInformation("No valid orders to process");
                }

                status = rejected == 0 ? IngestionStatus.Succeeded : IngestionStatus.PartiallySucceeded;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                status = IngestionStatus.Failed;
                _logger.LogError(ex, "Order ingestion failed");
                throw;
            }
            finally
            {
                _logger.LogInformation("Run {RunId} finished at {Time:u} UTC. Fetched {Fetched}, inserted {Inserted}, skipped {Skipped}, rejected {Rejected}, retries {Retries}.", runId, DateTime.UtcNow, fetched, inserted, skipped, rejected, RunMetricsContext.RetryCount);

                try
                {
                    await _logRepository.CompleteExecutionAsync(
                        logId,
                        DateTime.UtcNow,
                        status,
                        fetched,
                        inserted,
                        skipped,
                        rejected,
                        RunMetricsContext.RetryCount,
                        errorMessage,
                        ct);
                }
                catch (Exception logEx)
                {
                    _logger.LogError(logEx, "Failed to complete execution log for run {RunId}", runId);

                    // If the run itself failed, the original exception is already propagating.
                    // Swallow here so it is not masked by the log-write failure.
                    // If the run succeeded, the missing log row means we cannot claim success — rethrow.
                    if (status != IngestionStatus.Failed)
                    {
                        throw;
                    }
                }
                finally
                {
                    // Always clean up the run context, even if the completion write threw.
                    RunMetricsContext.EndRun();
                }
            }
        }
    }
}