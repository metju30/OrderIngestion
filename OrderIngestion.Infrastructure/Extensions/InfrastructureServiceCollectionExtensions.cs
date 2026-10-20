using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderIngestion.Application.Interfaces;
using OrderIngestion.Application.Services;
using OrderIngestion.Infrastructure.Importers;
using OrderIngestion.Infrastructure.Persistence;
using OrderIngestion.Infrastructure.Sinks;

namespace OrderIngestion.Infrastructure.Extensions;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // SQLite DbContext
        services.AddDbContext<OrderDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("DefaultConnection")));

        // Final store (always SQLite)
        services.AddScoped<IOrderRepository, SqliteOrderRepository>();
        services.AddScoped<IIngestionLogRepository, SqliteIngestionLogRepository>();

        // Run metrics (scoped to each ingestion run)
        services.AddScoped<IRunMetrics, RunMetrics>();

        // Mode switch: SQLite vs File queue
        var mode = configuration["Ingestion:Mode"] ?? "Sqlite";
        var queuePath = configuration["Ingestion:FileQueuePath"] ?? "data/orders-queue.jsonl";

        if (string.Equals(mode, "File", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<FileQueueLock>();
            services.AddSingleton<IOrderSink>(sp => new FileOrderSink(
                queuePath,
                sp.GetRequiredService<FileQueueLock>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<FileOrderSink>>()));

            services.AddScoped<IOrderImporter>(sp => new FileToSqliteImporter(
                queuePath,
                sp.GetRequiredService<IOrderRepository>(),
                sp.GetRequiredService<IIngestionLogRepository>(),
                sp.GetRequiredService<FileQueueLock>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<FileToSqliteImporter>>()));
        }
        else
        {
            services.AddScoped<IOrderSink, SqliteOrderSink>();
            services.AddScoped<IOrderImporter, NoOpOrderImporter>();
        }

        // External HTTP source + Polly
        var baseUrl = configuration["OrderSource:BaseUrl"] ?? "https://jsonplaceholder.typicode.com/";
        services.AddOrderSourceHttpClient(baseUrl);

        return services;
    }
}