namespace DictionaryImporter.AITextKit.AI.Infrastructure;

public interface IPerformanceMetricsCollector
{
    Task RecordMetricsAsync(ProviderMetrics metrics);

    Task<ProviderPerformance> GetProviderPerformanceAsync(
        string providerName,
        DateTime from,
        DateTime to);

    Task<IEnumerable<ProviderPerformance>> GetAllProvidersPerformanceAsync(DateTime from, DateTime to);
}