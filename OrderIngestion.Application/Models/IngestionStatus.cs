namespace OrderIngestion.Application.Models;

public enum IngestionStatus // belongs in Application because it is the layer that assigns it (Infrastructure only persists it). Infrastructure already references Application, so this stays layer-clean
{
    Running,
    Succeeded,
    PartiallySucceeded,
    Failed
}
