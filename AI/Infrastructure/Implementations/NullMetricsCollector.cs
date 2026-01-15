using DictionaryImporter.AI.Infrastructure.Telemetry;

namespace DictionaryImporter.AI.Infrastructure.Implementations;

public class NullMetricsCollector : IPerformanceMetricsCollector
{
    public Task RecordMetricsAsync(ProviderMetrics metrics) => Task.CompletedTask;

    public Task<ProviderPerformance> GetProviderPerformanceAsync(
        string providerName, DateTime from, DateTime to) =>
        Task.FromResult(new ProviderPerformance { Provider = providerName });

    public Task<IEnumerable<ProviderPerformance>> GetAllProvidersPerformanceAsync(
        DateTime from, DateTime to) =>
        Task.FromResult(Enumerable.Empty<ProviderPerformance>());
}

public class NullTelemetryService : ITelemetryService
{
    public Task RecordEventAsync(TelemetryEvent telemetryEvent) => Task.CompletedTask;

    public Task RecordMetricAsync(string name, double value, Dictionary<string, string> dimensions = null) => Task.CompletedTask;

    public Task RecordExceptionAsync(Exception exception, Dictionary<string, string> properties = null) => Task.CompletedTask;
}