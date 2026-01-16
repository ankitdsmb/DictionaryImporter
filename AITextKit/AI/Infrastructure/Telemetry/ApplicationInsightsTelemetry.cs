namespace DictionaryImporter.AITextKit.AI.Infrastructure.Telemetry;

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