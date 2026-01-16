using DictionaryImporter.AITextKit.AI.Infrastructure.Telemetry;

namespace DictionaryImporter.AITextKit.AI.Infrastructure.Implementations;

public class NullTelemetryService : ITelemetryService
{
    public Task RecordEventAsync(TelemetryEvent telemetryEvent) => Task.CompletedTask;

    public Task RecordMetricAsync(string name, double value, Dictionary<string, string> dimensions = null) => Task.CompletedTask;

    public Task RecordExceptionAsync(Exception exception, Dictionary<string, string> properties = null) => Task.CompletedTask;
}