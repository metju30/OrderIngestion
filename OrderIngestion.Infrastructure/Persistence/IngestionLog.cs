using OrderIngestion.Application.Models;

namespace OrderIngestion.Infrastructure.Persistence;

public class IngestionLog // this entity lives in Infrastructure because it's a persistence concern, not a domain concept.
{
    public int Id { get; set; }
    public IngestionRunKind RunKind { get; set; } = IngestionRunKind.Ingestion;
    public required string RunId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }   // null while Running
    public IngestionStatus Status { get; set; }

    public int FetchedCount { get; set; }
    public int InsertedCount { get; set; }
    public int SkippedCount { get; set; }
    public int RejectedCount { get; set; }
    public int RetryCount { get; set; }

    public string? ErrorMessage { get; set; }
}