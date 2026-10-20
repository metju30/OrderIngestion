namespace OrderIngestion.Domain.Entities;

public class Order
{
    public int Id { get; set; }           // DB PK, auto-increment
    public int ExternalId { get; set; }   // From the external API (e.g. JSONPlaceholder post id), unique
    public int UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime IngestedAtUtc { get; set; } = DateTime.UtcNow;
}