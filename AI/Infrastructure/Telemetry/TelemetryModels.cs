namespace DictionaryImporter.AI.Infrastructure.Telemetry;

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

public class TelemetryEvent
{
    public TelemetryEventType EventType { get; set; }
    public string RequestId { get; set; }
    public string ProviderName { get; set; }
    public string UserId { get; set; }
    public RequestType? RequestType { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Properties { get; set; } = new();
    public Dictionary<string, double> Metrics { get; set; } = new();
    public object Data { get; set; }
}

public interface ITelemetryService
{
    Task RecordEventAsync(TelemetryEvent telemetryEvent);

    Task RecordMetricAsync(string name, double value, Dictionary<string, string> dimensions = null);

    Task RecordExceptionAsync(Exception exception, Dictionary<string, string> properties = null);
}

public class ApplicationInsightsTelemetry(
    ILogger<ApplicationInsightsTelemetry> logger,
    IOptions<TelemetryConfiguration> config)
    : ITelemetryService
{
    private readonly TelemetryConfiguration _config = config.Value;

    public Task RecordEventAsync(TelemetryEvent telemetryEvent)
    {
        try
        {
            if (!_config.EnableTelemetry)
                return Task.CompletedTask;

            logger.LogDebug(
                "Telemetry event: {EventType} for request {RequestId}",
                telemetryEvent.EventType, telemetryEvent.RequestId);

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to record telemetry event");
            return Task.CompletedTask;
        }
    }

    public Task RecordMetricAsync(string name, double value, Dictionary<string, string> dimensions = null)
    {
        logger.LogDebug("Metric: {Name} = {Value}", name, value);
        return Task.CompletedTask;
    }

    public Task RecordExceptionAsync(Exception exception, Dictionary<string, string> properties = null)
    {
        logger.LogError(exception, "Exception recorded in telemetry");
        return Task.CompletedTask;
    }
}