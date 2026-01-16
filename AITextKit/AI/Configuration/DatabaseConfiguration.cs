namespace DictionaryImporter.AITextKit.AI.Configuration;

public class DatabaseConfiguration
{
    public string ConnectionString { get; set; }
    public int CommandTimeoutSeconds { get; set; } = 30;
    public int MaxPoolSize { get; set; } = 100;
    public int MinPoolSize { get; set; } = 10;
    public bool EnableRetryOnFailure { get; set; } = true;
    public int MaxRetryCount { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 1;
    public int ConnectionLifetimeMinutes { get; set; } = 30;
    public bool EnableConnectionPooling { get; set; } = true;
    public int ConnectionIdleTimeoutSeconds { get; set; } = 300;
}