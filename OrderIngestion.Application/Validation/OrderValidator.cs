using OrderIngestion.Domain.Entities;

namespace OrderIngestion.Application.Validation;

public static class OrderValidator
{
    public static bool IsValid(Order order, out string reason)
    {
        if (order.ExternalId <= 0) { reason = "ExternalId must be > 0"; return false; }
        if (string.IsNullOrWhiteSpace(order.Title)) { reason = "Title cannot be null or whitespace"; return false; }
        if (string.IsNullOrEmpty(order.Body)) { reason = "Body cannot be null"; return false; }
        if (order.UserId <= 0) { reason = "UserId must be > 0"; return false; }

        reason = string.Empty;
        return true;
    }
}