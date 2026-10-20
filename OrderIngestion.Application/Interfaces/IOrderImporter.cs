namespace OrderIngestion.Application.Interfaces;

public interface IOrderImporter
{
    /// <summary>Moves any pending orders from the intermediate store into the final store.</summary>
    Task<int> ImportPendingAsync(CancellationToken ct = default);
}