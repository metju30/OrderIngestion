using OrderIngestion.Application.Interfaces;

namespace OrderIngestion.Application.Services;

public class RunMetrics : IRunMetrics
{
    private int _retryCount;

    public int RetryCount => _retryCount;

    public void IncrementRetry()
    {
        Interlocked.Increment(ref _retryCount); // thread-safe retry counting
    }
}
