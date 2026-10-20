namespace OrderIngestion.Infrastructure.Sinks;

/// <summary>
/// Shared between FileOrderSink (writer) and FileToSqliteImporter (drainer)
/// so that the rename of the queue file is atomic w.r.t. new appends.
/// </summary>
public sealed class FileQueueLock : IDisposable
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
    public void Dispose() => Semaphore.Dispose();
}