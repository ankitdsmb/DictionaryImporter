namespace DictionaryImporter.AITextKit.AI.Infrastructure.Implementations;

public class QuotaManagerOptions
{
    public int DefaultRequestLimit { get; set; } = 100;
    public long DefaultTokenLimit { get; set; } = 1000000;
    public int DefaultRequestsPerMinute { get; set; } = 60;
    public int DefaultRequestsPerHour { get; set; } = 1000;
    public int DefaultRequestsPerDay { get; set; } = 10000;
    public int DefaultRequestsPerMonth { get; set; } = 100000;
    public long DefaultTokensPerMinute { get; set; } = 90000;
    public long DefaultTokensPerHour { get; set; } = 500000;
    public long DefaultTokensPerDay { get; set; } = 2000000;
    public long DefaultTokensPerMonth { get; set; } = 10000000;
    public int CleanupIntervalMinutes { get; set; } = 5;
}