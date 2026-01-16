namespace DictionaryImporter.AITextKit.AI.Infrastructure.Implementations;

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