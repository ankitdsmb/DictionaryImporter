namespace DictionaryImporter.AI.Configuration
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

    public class ProviderCapabilitiesConfiguration
    {
        public bool TextCompletion { get; set; } = true;
        public bool ChatCompletion { get; set; } = false;
        public bool ImageGeneration { get; set; } = false;
        public bool ImageAnalysis { get; set; } = false;
        public bool AudioTranscription { get; set; } = false;
        public bool TextToSpeech { get; set; } = false;
        public List<string> SupportedLanguages { get; set; } = new() { "en" };
        public List<string> SupportedImageFormats { get; set; } = new();
        public List<string> SupportedAudioFormats { get; set; } = new();
        public int MaxTokensLimit { get; set; } = 4096;
        public int MaxImageSize { get; set; } = 1024;
    }

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

    public class CacheConfiguration
    {
        public string Type { get; set; } = "Memory";
        public string ConnectionString { get; set; }
        public int DefaultExpirationMinutes { get; set; } = 60;
        public int MaxCacheSizeMB { get; set; } = 100;
        public bool EnableCompression { get; set; } = true;
    }

    public class TelemetryConfiguration
    {
        public bool EnableTelemetry { get; set; } = true;
        public string Provider { get; set; } = "ApplicationInsights";
        public string ConnectionString { get; set; }
        public string InstrumentationKey { get; set; }
        public int MetricsExportIntervalSeconds { get; set; } = 30;
        public string LogLevel { get; set; } = "Information";
        public bool EnableRequestTracking { get; set; } = true;
        public bool EnableDependencyTracking { get; set; } = true;
        public bool EnablePerformanceCounters { get; set; } = true;
    }

    public class SecurityConfiguration
    {
        public bool EnableApiKeyRotation { get; set; } = false;
        public int ApiKeyRotationDays { get; set; } = 30;
        public string KeyVaultUrl { get; set; }
        public bool EnableContentValidation { get; set; } = true;
        public List<string> BlockedPatterns { get; set; } = new();
        public bool EnableUserQuotas { get; set; } = true;
        public int DefaultUserRequestsPerDay { get; set; } = 100;
        public long DefaultUserTokensPerDay { get; set; } = 100000;
    }

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

    public class FreeTierLimits
    {
        public int MaxTokens { get; set; } = 1000;
        public int MaxRequestsPerDay { get; set; } = 100;
        public int MaxImagesPerMonth { get; set; } = 50;
        public int MaxAudioMinutesPerMonth { get; set; } = 60;
        public int MaxCharactersPerMonth { get; set; } = 10000;
    }

    public class DatabaseOptions
    {
        public string ConnectionString { get; set; }
        public int CommandTimeoutSeconds { get; set; } = 30;
        public int MaxPoolSize { get; set; } = 100;
        public bool EnableRetryOnFailure { get; set; } = true;
        public int MaxRetryCount { get; set; } = 3;
        public int RetryDelaySeconds { get; set; } = 1;
    }

    public class AuditLoggerOptions
    {
        public bool Enabled { get; set; } = true;
        public bool UseBatching { get; set; } = true;
        public int BatchIntervalSeconds { get; set; } = 5;
        public int MaxBatchSize { get; set; } = 100;
    }

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
}