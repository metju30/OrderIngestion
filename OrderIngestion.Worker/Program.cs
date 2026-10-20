using OrderIngestion.Application.Services;
using OrderIngestion.Infrastructure.Extensions;
using OrderIngestion.Infrastructure.Persistence;
using OrderIngestion.Worker.Options;
using Serilog;

namespace OrderIngestion.Worker
{
    public class Program
    {
        public static void Main(string[] args)
        {
            //This is the host: it wires everything together, schedules execution, and configures logging/monitoring.

            var builder = Host.CreateApplicationBuilder(args); // sets up configuration, logging, DI, and hosted services in one line — this is the modern Generic Host pattern for Worker Services

            // Serilog
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(builder.Configuration)
                .Enrich.FromLogContext()
                .CreateLogger();

            builder.Logging.ClearProviders();
            builder.Logging.AddSerilog(Log.Logger);

            builder.Services
            .AddOptions<IngestionOptions>()
            .Bind(builder.Configuration.GetSection("Ingestion"))
            .Validate(o => o.IntervalMinutes is > 0 and <= 1440, "Ingestion:IntervalMinutes must be between 1 and 1440.")
            .Validate(o => o.ImportIntervalSeconds is > 0 and <= 3600, "Ingestion:ImportIntervalSeconds must be between 1 and 3600.")
            .ValidateOnStart();

            // Application Services
            builder.Services.AddScoped<OrderIngestionService>();

            // Infrastructure (DB + HTTP + Polly)
            builder.Services.AddInfrastructure(builder.Configuration);

            // Workers
            builder.Services.AddHostedService<Worker>();
            builder.Services.AddHostedService<OrderImporterWorker>();

            // Optional: Application Insights (monitoring)
            var aiConn = builder.Configuration["ApplicationInsights:ConnectionString"];
            if (!string.IsNullOrWhiteSpace(aiConn))
            {
                builder.Services.AddApplicationInsightsTelemetryWorkerService(options =>
                {
                    options.ConnectionString = aiConn;
                });
            }

            var host = builder.Build();

            // Ensure the SQLite schema exists. EnsureCreated is idempotent: it does nothing if the DB is up to date.
            // For this demo we use EnsureCreated; a production build would use EF Core migrations.
            using (var scope = host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
                db.Database.EnsureCreated();
            }

            try
            {
                Log.Information("Order Ingestion Worker starting...");
                host.Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Host terminated unexpectedly");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}