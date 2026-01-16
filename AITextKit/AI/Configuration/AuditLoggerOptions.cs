namespace DictionaryImporter.AITextKit.AI.Configuration;

public class AuditLoggerOptions
{
    public bool Enabled { get; set; } = true;
    public bool UseBatching { get; set; } = true;
    public int BatchIntervalSeconds { get; set; } = 5;
    public int MaxBatchSize { get; set; } = 100;
}