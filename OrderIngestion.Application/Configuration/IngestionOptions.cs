namespace OrderIngestion.Worker.Options;

public class IngestionOptions
{
    public string Mode { get; set; } = "Sqlite";
    public string FileQueuePath { get; set; } = "data/orders-queue.jsonl";
    public int IntervalMinutes { get; set; } = 15;
    public int ImportIntervalSeconds { get; set; } = 30;
}