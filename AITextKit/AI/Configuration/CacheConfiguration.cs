namespace DictionaryImporter.AITextKit.AI.Configuration;

public class CacheConfiguration
{
    public string Type { get; set; } = "Memory";
    public string ConnectionString { get; set; }
    public int DefaultExpirationMinutes { get; set; } = 60;
    public int MaxCacheSizeMB { get; set; } = 100;
    public bool EnableCompression { get; set; } = true;
}