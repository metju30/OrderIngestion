using Microsoft.Extensions.Options;
using OrderIngestion.Application.Interfaces;
using OrderIngestion.Worker.Options;

namespace OrderIngestion.Worker;

/// <summary>
/// Periodically drains the intermediate queue (file) into SQLite.
/// No-op in SQLite mode (NoOpOrderImporter returns instantly).
/// </summary>
public class OrderImporterWorker : BackgroundService
{
    private readonly ILogger<OrderImporterWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _interval;

    public OrderImporterWorker(
        ILogger<OrderImporterWorker> logger,
        IServiceScopeFactory scopeFactory,
        IOptions<IngestionOptions> options)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _interval = TimeSpan.FromSeconds(options.Value.ImportIntervalSeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var _ = _logger.BeginScope(new Dictionary<string, object>
        {
            ["RunKind"] = "Import",
        });

        _logger.LogInformation("Importer worker started. Interval: {Interval}", _interval);

        using var timer = new PeriodicTimer(_interval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var importer = scope.ServiceProvider.GetRequiredService<IOrderImporter>();
                var n = await importer.ImportPendingAsync(stoppingToken);
                if (n > 0)
                    _logger.LogInformation("Importer moved {Count} orders to SQLite", n);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Importer run failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}