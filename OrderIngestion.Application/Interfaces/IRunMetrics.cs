namespace OrderIngestion.Application.Interfaces;

/// <summary>
/// Per-run metrics seam. Registered as Scoped so callers inside the ingestion
/// scope can record runtime metrics (e.g. elapsed time, orders seen) without
/// coupling to a specific telemetry backend.
///
/// NOTE: Retry counts are NOT exposed here. Polly's retry callback runs inside
/// the HttpMessageHandler chain, which is pooled/singleton and lives outside
/// the per-run DI scope. Retry counts flow through <c>RunMetricsContext</c>
/// (AsyncLocal) instead — see ADR-003 for the reasoning.
/// </summary>
public interface IRunMetrics
{
    int RetryCount { get; } // see note above: reads from RunMetricsContext, not from a scope field
    void IncrementRetry();
}
