using OrderIngestion.Application.Models;

namespace OrderIngestion.Application.Interfaces;

public interface IIngestionLogRepository
{
    /// <summary>Inserts a row with Status=Running and FinishedAtUtc=null; returns the row id.</summary>
    Task<int> StartExecutionAsync(string runId, DateTime startedAtUtc, IngestionRunKind kind, CancellationToken ct = default);

    /// <summary>Updates the row created by StartExecutionAsync to its final state.</summary>
    Task CompleteExecutionAsync(
        int logId,
        DateTime finishedAtUtc,
        IngestionStatus status,
        int fetched,
        int inserted,
        int skipped,
        int rejected,
        int retry,
        string? errorMessage,
        CancellationToken ct = default);
}