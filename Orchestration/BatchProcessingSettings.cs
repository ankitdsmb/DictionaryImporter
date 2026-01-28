namespace DictionaryImporter.Orchestration;

public class BatchProcessingSettings
{
    public string ConnectionString { get; set; } = string.Empty;
    public int BatchSize { get; set; } = 1000;
    public int MaxParallelism { get; set; } = 4;
    public int MaxDatabaseConnections { get; set; } = 2;
    public int NonEnglishTextParallelism { get; set; } = 10;
    public int CommandTimeoutSeconds { get; set; } = 120;
    public int BatchCommandTimeoutSeconds { get; set; } = 180;

    public static BatchProcessingSettings Default => new()
    {
        ConnectionString = string.Empty,
        BatchSize = 1000,
        MaxParallelism = 4,
        MaxDatabaseConnections = 2,
        NonEnglishTextParallelism = 10,
        CommandTimeoutSeconds = 120,
        BatchCommandTimeoutSeconds = 180
    };
}