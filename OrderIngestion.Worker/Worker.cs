using Microsoft.Extensions.Options;
using OrderIngestion.Application.Services;
using OrderIngestion.Worker.Options;

namespace OrderIngestion.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceScopeFactory _scopeFactory; // BackgroundService is registered as a singleton, but OrderDbContext is scoped. You must inject IServiceScopeFactory and create a scope for each execution. Injecting OrderIngestionService directly would cause a captive dependency error
    private readonly TimeSpan _interval;

    public Worker(
        ILogger<Worker> logger,
        IServiceScopeFactory scopeFactory,
        IOptions<IngestionOptions> options)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _interval = TimeSpan.FromMinutes(options.Value.IntervalMinutes); // Strongly-typed access
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var _ = _logger.BeginScope(new Dictionary<string, object>
        {
            ["RunKind"] = "Ingestion",
        });

        _logger.LogInformation("Worker starting. Interval: {Interval}", _interval);
        try
        {
            // Run immediately on startup
            await RunIngestionAsync(stoppingToken);

            // Then run periodically
            using var timer = new PeriodicTimer(_interval); // This avoids overlapping execution ticks if an execution takes longer than 15 minutes.
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunIngestionAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Worker cancellation requested");
        }
        finally
        {
            _logger.LogInformation("Worker exiting cleanly");
        }
    }

    private async Task RunIngestionAsync(CancellationToken ct)
    {
        try
        {
            // IMPORTANT(Captive Dependency Prevention): Worker is singleton; DbContext is scoped. Create a new scope for each execution.
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<OrderIngestionService>();
            await service.RunAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Ingestion cancelled due to shutdown");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error during ingestion run");
            // Do NOT rethrow — let the next scheduled run proceed.
        }
    }
}