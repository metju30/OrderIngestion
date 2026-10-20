using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
using OrderIngestion.Application.Interfaces;
using OrderIngestion.Application.Models;

namespace OrderIngestion.Infrastructure.Extensions;

public static class HttpClientExtensions
{
    public static IHttpClientBuilder AddOrderSourceHttpClient(
        this IServiceCollection services, string baseUrl)
    {
        return services
            .AddHttpClient<IOrderSource, External.JsonPlaceholderOrderSource>(c =>
            {
                c.BaseAddress = new Uri(baseUrl);
                c.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddPolicyHandler((sp, _) => GetRetryPolicy());
    }

    /// <summary>
    /// The HandleTransientHttpError() extension covers HTTP 5xx responses, HTTP 408 (Request Timeout), and HttpRequestException — exactly the transient faults Polly is designed for. 
    /// Exponential backoff (2s, 4s, 8s) prevents hammering a struggling API.
    /// Increments RunMetricsContext.RetryCount on each retry.
    /// </summary>
    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError() // 5xx, 408, HttpRequestException
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                onRetry: (outcome, delay, attempt, _) =>
                {
                    RunMetricsContext.IncrementRetry();
                    Console.WriteLine($"Retry {attempt} after {delay.TotalSeconds}s due to: {outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()}");
                });
    }
}