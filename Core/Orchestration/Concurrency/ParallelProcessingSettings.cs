namespace DictionaryImporter.Core.Orchestration.Concurrency;

public class ParallelProcessingSettings
{
    public int DegreeOfParallelism { get; set; } = 4;
    public int MaxConcurrentBatches { get; set; } = 2;
    public int BatchSize { get; set; } = 1000;
    public int MaxDatabaseConnections { get; set; } = 10;
    public bool EnableParallelProcessing { get; set; } = true;
    public int RetryCount { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 1000;

    public static ParallelProcessingSettings Default => new()
    {
        DegreeOfParallelism = 4,
        MaxConcurrentBatches = 2,
        BatchSize = 1000,
        MaxDatabaseConnections = 10,
        EnableParallelProcessing = true,
        RetryCount = 3,
        RetryDelayMs = 1000
    };
}