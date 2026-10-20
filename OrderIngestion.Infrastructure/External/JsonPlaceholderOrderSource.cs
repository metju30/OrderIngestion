using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using OrderIngestion.Application.Interfaces;
using OrderIngestion.Domain.Entities;

namespace OrderIngestion.Infrastructure.External;

public class JsonPlaceholderOrderSource : IOrderSource // JSONPlaceholder's /posts endpoint returns 100 posts — perfect as mock orders. Each post has id, userId, title, and body
{
    private readonly HttpClient _http;
    private readonly ILogger<JsonPlaceholderOrderSource> _logger;

    public JsonPlaceholderOrderSource(HttpClient http, ILogger<JsonPlaceholderOrderSource> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Order>> FetchOrdersAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Fetching orders from {BaseAddress}", _http.BaseAddress);

        var posts = await _http.GetFromJsonAsync<List<PostDto>>("posts", ct);
        if (posts is null) return Array.Empty<Order>();

        return posts.Select(p => new Order
        {
            ExternalId = p.Id,
            UserId = p.UserId,
            Title = p.Title,
            Body = p.Body
        }).ToList();
    }

    private class PostDto
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
    }
}