namespace DictionaryImporter.AITextKit.AI.Configuration
{
    public class AiOrchestrationConfiguration
    {
        public bool EnableOrchestration { get; set; } = true;
        public bool EnableFallback { get; set; } = true;
        public bool EnableCaching { get; set; } = true;
        public int CacheDurationMinutes { get; set; } = 60;
        public bool EnableRateLimiting { get; set; } = true;
        public bool EnableQuotaManagement { get; set; } = true;
        public bool EnableAuditLogging { get; set; } = true;
        public bool EnableMetricsCollection { get; set; } = true;
        public int MaxConcurrentRequests { get; set; } = 20;
        public int DefaultTimeoutSeconds { get; set; } = 30;
        public int HealthCheckIntervalSeconds { get; set; } = 60;
        public string DefaultProvider { get; set; } = "OpenRouter";
        public int FailureHistorySize { get; set; } = 100;
        public List<string> FallbackOrder { get; set; } = new();
        public Dictionary<string, ProviderConfiguration> Providers { get; set; } = new();
    }
}