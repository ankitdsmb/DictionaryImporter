using DictionaryImporter.AI.Core.Contracts;

namespace DictionaryImporter.AI.Extensions;

public class OrchestrationHealthCheck
{
    private readonly ICompletionOrchestrator _orchestrator;
    private readonly ILogger<OrchestrationHealthCheck> _logger;

    public OrchestrationHealthCheck(
        ICompletionOrchestrator orchestrator,
        ILogger<OrchestrationHealthCheck> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _orchestrator.HealthCheckAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return false;
        }
    }
}