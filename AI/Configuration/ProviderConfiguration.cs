namespace DictionaryImporter.AI.Configuration;

public class ProviderConfiguration
{
    public string Name { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Priority { get; set; } = 10;
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetries { get; set; } = 2;
    public int CircuitBreakerFailuresBeforeBreaking { get; set; } = 5;
    public int CircuitBreakerDurationSeconds { get; set; } = 30;
    public bool EnableCaching { get; set; } = true;
    public int CacheDurationMinutes { get; set; } = 60;
    public bool EnableRateLimiting { get; set; } = true;
    public int RequestsPerMinute { get; set; } = 60;
    public int RequestsPerDay { get; set; } = 1000;
    public long TokensPerMinute { get; set; } = 90000;
    public long TokensPerDay { get; set; } = 1000000;
    public decimal CostLimitPerDay { get; set; } = 10.00m;
    public ProviderCapabilitiesConfiguration Capabilities { get; set; } = new();
    public Dictionary<string, string> AdditionalSettings { get; set; } = new();
    public FreeTierLimits FreeTier { get; set; } = new();
}