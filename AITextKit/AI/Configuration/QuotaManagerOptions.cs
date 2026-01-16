namespace DictionaryImporter.AITextKit.AI.Configuration;

public class QuotaManagerOptions
{
    public int DefaultRequestLimit { get; set; } = 100;
    public int DefaultTokenLimit { get; set; } = 100000;
    public int DefaultRequestsPerMinute { get; set; } = 60;
    public int DefaultRequestsPerHour { get; set; } = 1000;
    public int DefaultRequestsPerDay { get; set; } = 10000;
    public int DefaultRequestsPerMonth { get; set; } = 300000;
    public int DefaultTokenLimitPerMinute { get; set; } = 90000;
    public int DefaultTokenLimitPerHour { get; set; } = 900000;
    public int DefaultTokenLimitPerDay { get; set; } = 10000000;
    public int DefaultTokenLimitPerMonth { get; set; } = 300000000;
    public int CleanupIntervalMinutes { get; set; } = 30;
}