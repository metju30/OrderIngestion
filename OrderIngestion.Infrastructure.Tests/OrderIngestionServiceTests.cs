using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OrderIngestion.Application.Interfaces;
using OrderIngestion.Application.Models;
using OrderIngestion.Application.Services;
using OrderIngestion.Domain.Entities;
using OrderIngestion.Infrastructure.Tests.Fakes;

namespace OrderIngestion.Infrastructure.Tests;

public class OrderIngestionServiceTests
{
    private static OrderIngestionService Build(
        IOrderSource source,
        IOrderSink? sink = null,
        FakeIngestionLogRepository? log = null)
        => new(source,
               sink ?? new FakeOrderSink(),
               log ?? new FakeIngestionLogRepository(),
               new FakeRunMetrics(),
               NullLogger<OrderIngestionService>.Instance);

    [Fact]
    public async Task RunAsync_WhenSourceThrowsHttpRequestException_ShouldRecordFailedAndRethrow()
    {
        var log = new FakeIngestionLogRepository();
        var svc = Build(FakeOrderSource.Throws(new HttpRequestException("503 Service Unavailable")), log: log);

        await Assert.ThrowsAsync<HttpRequestException>(() => svc.RunAsync());

        var c = Assert.Single(log.Completions);
        Assert.Equal(IngestionStatus.Failed, c.Status);
        Assert.Contains("503", c.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_WhenSourceThrowsJsonException_ShouldRecordFailedAndRethrow()
    {
        var log = new FakeIngestionLogRepository();
        var svc = Build(FakeOrderSource.Throws(new JsonException("bad json")), log: log);

        await Assert.ThrowsAsync<JsonException>(() => svc.RunAsync());

        var c = Assert.Single(log.Completions);
        Assert.Equal(IngestionStatus.Failed, c.Status);
    }

    [Fact]
    public async Task RunAsync_WhenAllOrdersValid_ShouldCallSinkAndRecordSucceeded()
    {
        var sink = new FakeOrderSink { NextResult = (2, 1) };
        var log = new FakeIngestionLogRepository();
        var orders = new[]
        {
            new Order { ExternalId = 1, UserId = 1, Title = "A", Body = "B" },
            new Order { ExternalId = 2, UserId = 1, Title = "A", Body = "B" },
        };
        var svc = Build(FakeOrderSource.Returns(orders), sink, log);

        await svc.RunAsync();

        Assert.Single(sink.Batches);
        var c = Assert.Single(log.Completions);
        Assert.Equal(IngestionStatus.Succeeded, c.Status);
        Assert.Equal(2, c.Inserted);
        Assert.Equal(1, c.Skipped);
        Assert.Equal(0, c.Rejected);
    }

    [Fact]
    public async Task RunAsync_WhenSomeOrdersInvalid_ShouldRejectAndRecordPartiallySucceeded()
    {
        var sink = new FakeOrderSink { NextResult = (1, 0) };
        var log = new FakeIngestionLogRepository();
        var orders = new[]
        {
            new Order { ExternalId = 1, UserId = 1, Title = "A", Body = "B" }, // valid
            new Order { ExternalId = 0, UserId = 1, Title = "A", Body = "B" }, // invalid
        };
        var svc = Build(FakeOrderSource.Returns(orders), sink, log);

        await svc.RunAsync();

        var c = Assert.Single(log.Completions);
        Assert.Equal(IngestionStatus.PartiallySucceeded, c.Status);
        Assert.Equal(1, c.Rejected);
    }

    [Fact]
    public async Task RunAsync_WhenSourceReturnsEmpty_ShouldSucceedWithZeroCounts()
    {
        var sink = new FakeOrderSink();
        var log = new FakeIngestionLogRepository();
        var svc = Build(FakeOrderSource.Returns(), sink, log);

        await svc.RunAsync();

        Assert.Empty(sink.Batches); // sink never called for an empty batch
        var c = Assert.Single(log.Completions);
        Assert.Equal(IngestionStatus.Succeeded, c.Status);
        Assert.Equal(0, c.Fetched);
        Assert.Equal(0, c.Inserted);
        Assert.Equal(0, c.Skipped);
        Assert.Equal(0, c.Rejected);
    }
}