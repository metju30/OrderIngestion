using Microsoft.Extensions.Logging.Abstractions;
using OrderIngestion.Infrastructure.External;
using System.Net;
using System.Text;
using System.Text.Json;

namespace OrderIngestion.Infrastructure.Tests;

public class OrderIngestionClientTests
{
    // Network Level: Simulates what happens when the internet drops or the target server crashes
    // Scenario 1: Simulate Transient Network Error (503 Service Unavailable)
    [Fact]
    public async Task FetchOrdersAsync_WhenTransientErrorOccurs_ShouldThrowHttpRequestException()
    {
        // 1. Arrange the simulated network failure
        var mockHandler = new MockHttpMessageHandler(request => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.ServiceUnavailable,
            Content = new StringContent("Service Unavailable")
        });

        using var httpClient = new HttpClient(mockHandler)
        {
            BaseAddress = new Uri("https://typicode.com")
        };

        // 2. Inject the client and a NullLogger into your actual concrete Source
        var orderSource = new JsonPlaceholderOrderSource(httpClient, NullLogger<JsonPlaceholderOrderSource>.Instance);

        // 3. Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await orderSource.FetchOrdersAsync();
        });
    }

    // Payload Level: Simulates receiving corrupted text instead of structured data. 
    // Scenario 2: Simulate Malformed JSON Parsing Errors
    [Fact]
    public async Task FetchOrdersAsync_WhenJsonIsMalformed_ShouldThrowJsonException()
    {
        // 1. Arrange broken JSON response payload
        var brokenJson = "[ { \"userId\": 1, \"id\": 1, \"title\": \"broken payload... ";

        var mockHandler = new MockHttpMessageHandler(request => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(brokenJson, Encoding.UTF8, "application/json")
        });

        using var httpClient = new HttpClient(mockHandler)
        {
            BaseAddress = new Uri("https://typicode.com")
        };

        var orderSource = new JsonPlaceholderOrderSource(httpClient, NullLogger<JsonPlaceholderOrderSource>.Instance);

        // 2. Act & Assert
        await Assert.ThrowsAsync<JsonException>(async () =>
        {
            await orderSource.FetchOrdersAsync();
        });
    }

    // Business Level: Simulates a scenario where the JSON arrives perfectly formed, but fields like id or price are missing. 
    // Scenario 3: Missing Critical Data Fields (Demo Validation)
    [Fact]
    public async Task FetchOrdersAsync_WhenRequiredDataIsMissing_ShouldHandleValidation()
    {
        // 1. Arrange JSON where fields don't match your PostDto schema expectations
        var missingDataJson = "[ { \"userId\": 1, \"body\": \"Missing Title and ID!\" } ]";

        var mockHandler = new MockHttpMessageHandler(request => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(missingDataJson, Encoding.UTF8, "application/json")
        });

        using var httpClient = new HttpClient(mockHandler)
        {
            BaseAddress = new Uri("https://typicode.com")
        };

        var orderSource = new JsonPlaceholderOrderSource(httpClient, NullLogger<JsonPlaceholderOrderSource>.Instance);

        // 2. Act
        var result = await orderSource.FetchOrdersAsync();

        // 3. Assert (Depending on how your PostDto handles missing properties, 
        // it might map ID to 0 or title to null. Verify your specific validation strategy here)
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        // e.g., Assert.Null(result[0].Title); or check if your mapping throws a custom domain error
    }
}
