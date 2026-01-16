namespace DictionaryImporter.AITextKit.AI.Configuration;

public class DatabaseOptions
{
    public string ConnectionString { get; set; }
    public int CommandTimeoutSeconds { get; set; } = 30;
    public int MaxPoolSize { get; set; } = 100;
    public bool EnableRetryOnFailure { get; set; } = true;
    public int MaxRetryCount { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 1;
}