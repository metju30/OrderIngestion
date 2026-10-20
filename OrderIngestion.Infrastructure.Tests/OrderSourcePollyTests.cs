using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OrderIngestion.Application.Interfaces;
using OrderIngestion.Application.Models;
using OrderIngestion.Application.Services;
using OrderIngestion.Infrastructure.Extensions;
using System.Net;

namespace OrderIngestion.Infrastructure.Tests;

public class OrderSourcePollyTests
{
    [Fact]
    public async Task AddOrderSourceHttpClient_On503TransientError_ShouldTriggerPollyRetries()
    {
        // --- 1. ARRANGE ---
        int requestCounter = 0;

        // Create a mock handler that increments a counter on every hit and always returns 503
        var mockHandler = new MockHttpMessageHandler(request =>
        {
            Interlocked.Increment(ref requestCounter);
            return new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.ServiceUnavailable,
                Content = new StringContent("Transient Error")
            };
        });

        // Spin up an in-memory Service Collection (simulating your Worker's Startup composition)
        var services = new ServiceCollection();

        // Register fundamental dependencies required by your architecture
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScoped<IRunMetrics, RunMetrics>();

        // Call your actual production extension method to build the client and policies
        services.AddOrderSourceHttpClient("https://typicode.com")
                .ConfigurePrimaryHttpMessageHandler(() => mockHandler); // Inject our mock into the pipeline

        var serviceProvider = services.BuildServiceProvider();
        var orderSource = serviceProvider.GetRequiredService<IOrderSource>();

        // --- 2. ACT & ASSERT ---
        RunMetricsContext.StartRun();
        try
        {
            // The policy will eventually bubble up an HttpRequestException after exhausting retries
            await Assert.ThrowsAsync<HttpRequestException>(async () =>
            {
                await orderSource.FetchOrdersAsync();
            });

            // For example, if your policy retries 3 times, total requests should equal 4 (1 initial + 3 retries)

            // Assert that the retry counter is 3.
            Assert.Equal(3, RunMetricsContext.RetryCount);

            // Assert that the request counter is 4.
            Assert.Equal(4, requestCounter);
        }
        finally { RunMetricsContext.EndRun(); }
    }
}
