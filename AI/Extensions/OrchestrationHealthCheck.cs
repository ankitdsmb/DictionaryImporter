using DictionaryImporter.AI.Core.Contracts;

namespace DictionaryImporter.AI.Extensions;

public class OrchestrationHealthCheck(
    ICompletionOrchestrator orchestrator,
    ILogger<OrchestrationHealthCheck> logger)
{
    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await orchestrator.HealthCheckAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Health check failed");
            return false;
        }
    }
}