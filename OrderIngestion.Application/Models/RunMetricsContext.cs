namespace OrderIngestion.Application.Models;

/// <summary>
/// Run-scoped retry counter that flows across async calls, including through the
/// IHttpClientFactory handler chain. A scoped <c>IRunMetrics</c> cannot be used inside
/// AddPolicyHandler because the handler pipeline is pooled and does not see DI scopes.
/// </summary>
public static class RunMetricsContext
{
    private static readonly AsyncLocal<Counter?> _current = new();

    /// <summary>Start a new logical run and reset the counter.</summary>
    public static void StartRun() => _current.Value = new Counter();

    /// <summary>End the current run. Does not throw if no run is active.</summary>
    public static void EndRun() => _current.Value = null;

    /// <summary>Increment the retry counter for the current run. No-op if no run is active.</summary>
    public static void IncrementRetry() => Interlocked.Increment(ref (_current.Value ??= new Counter()).Count);

    /// <summary>Current retry count for the active run, or 0 if no run is active.</summary>
    public static int RetryCount => _current.Value?.Count ?? 0;

    private sealed class Counter { public int Count; }
}