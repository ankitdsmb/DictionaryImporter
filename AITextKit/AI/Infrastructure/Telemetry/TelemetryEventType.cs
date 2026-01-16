namespace DictionaryImporter.AITextKit.AI.Infrastructure.Telemetry;

public enum TelemetryEventType
{
    RequestStarted,
    RequestCompleted,
    RequestFailed,
    ProviderCalled,
    ProviderFailed,
    CacheHit,
    CacheMiss,
    QuotaExceeded,
    RateLimitExceeded,
    CircuitBreakerOpened,
    HealthCheckPerformed
}