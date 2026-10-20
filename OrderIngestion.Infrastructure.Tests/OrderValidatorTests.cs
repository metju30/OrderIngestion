using OrderIngestion.Application.Validation;
using OrderIngestion.Domain.Entities;

namespace OrderIngestion.Infrastructure.Tests;

public class OrderValidatorTests
{
    private static Order Valid() => new() { ExternalId = 1, UserId = 1, Title = "T", Body = "B" };

    [Fact]
    public void IsValid_ValidOrder_ReturnsTrue() => Assert.True(OrderValidator.IsValid(Valid(), out _));

    [Theory, InlineData(0), InlineData(-1)]
    public void IsValid_InvalidExternalId_ReturnsFalse(int id)
    {
        var o = Valid(); o.ExternalId = id;
        Assert.False(OrderValidator.IsValid(o, out var r));
        Assert.Equal("ExternalId must be > 0", r);
    }

    [Theory, InlineData(null), InlineData(""), InlineData("   ")]
    public void IsValid_InvalidTitle_ReturnsFalse(string? title)
    {
        var o = Valid(); o.Title = title!;
        Assert.False(OrderValidator.IsValid(o, out var r));
        Assert.Equal("Title cannot be null or whitespace", r);
    }

    [Fact]
    public void IsValid_EmptyBody_ReturnsFalse()
    {
        var o = Valid(); o.Body = "";
        Assert.False(OrderValidator.IsValid(o, out var r));
        Assert.Equal("Body cannot be null", r);
    }

    [Fact]
    public void IsValid_InvalidUserId_ReturnsFalse()
    {
        var o = Valid(); o.UserId = 0;
        Assert.False(OrderValidator.IsValid(o, out var r));
        Assert.Equal("UserId must be > 0", r);
    }
}