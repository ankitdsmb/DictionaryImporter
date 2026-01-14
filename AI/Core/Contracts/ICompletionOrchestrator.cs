namespace DictionaryImporter.AI.Core.Contracts
{
    public interface ICompletionOrchestrator
    {
        Task<AiResponse> GetCompletionAsync(
            AiRequest request,
            CancellationToken cancellationToken = default);

        Task<OrchestrationMetrics> GetMetricsAsync();

        Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default);

        IReadOnlyList<ProviderStatus> GetProviderStatuses();

        IReadOnlyList<ProviderFailure> GetRecentFailures();

        IReadOnlyDictionary<string, ProviderPerformance> GetPerformanceMetrics();

        IEnumerable<string> GetAvailableProviders();

        IEnumerable<string> GetProvidersByCapability(string capability);
    }
}