namespace DictionaryImporter.AITextKit.AI.Infrastructure.Telemetry;

public interface ITelemetryService
{
    Task RecordEventAsync(TelemetryEvent telemetryEvent);

    Task RecordMetricAsync(string name, double value, Dictionary<string, string> dimensions = null);

    Task RecordExceptionAsync(Exception exception, Dictionary<string, string> properties = null);
}