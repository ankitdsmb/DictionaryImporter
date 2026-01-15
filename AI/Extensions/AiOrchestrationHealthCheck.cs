using DictionaryImporter.AI.Core.Contracts;
using DictionaryImporter.AI.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DictionaryImporter.AI.Extensions;

public class AiOrchestrationHealthCheck(
    ICompletionOrchestrator orchestrator,
    IQuotaManager quotaManager,
    ILogger<AiOrchestrationHealthCheck> logger)
    : IHealthCheck
{
    private readonly ICompletionOrchestrator _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var healthData = new Dictionary<string, object>();
        var unhealthyReasons = new List<string>();

        try
        {
            var orchestratorHealthy = await _orchestrator.HealthCheckAsync(cancellationToken);
            if (!orchestratorHealthy)
            {
                unhealthyReasons.Add("Orchestrator health check failed");
            }

            healthData["orchestrator_healthy"] = orchestratorHealthy;

            var providerStatuses = _orchestrator.GetProviderStatuses();
            var healthyProviders = providerStatuses.Count(s => s.IsHealthy);
            var totalProviders = providerStatuses.Count;

            healthData["healthy_providers"] = healthyProviders;
            healthData["total_providers"] = totalProviders;
            healthData["provider_health_percentage"] = totalProviders > 0
                ? (healthyProviders * 100.0) / totalProviders
                : 0;

            if (healthyProviders == 0 && totalProviders > 0)
            {
                unhealthyReasons.Add("No healthy providers available");
            }

            var recentFailures = _orchestrator.GetRecentFailures();
            var recentFailureCount = recentFailures.Count(f =>
                f.FailureTime > DateTime.UtcNow.AddMinutes(-5));

            healthData["recent_failures_5min"] = recentFailureCount;

            if (recentFailureCount > 10)
            {
                unhealthyReasons.Add($"High failure rate: {recentFailureCount} failures in last 5 minutes");
            }

            if (quotaManager != null)
            {
                var providers = _orchestrator.GetAvailableProviders();
                var quotaStatuses = new Dictionary<string, object>();

                foreach (var provider in providers)
                {
                    try
                    {
                        var quotas = await quotaManager.GetProviderQuotasAsync(provider);
                        var exhaustedQuotas = quotas.Count(q => q.IsExhausted);

                        quotaStatuses[provider] = new
                        {
                            total_quotas = quotas.Count(),
                            exhausted_quotas = exhaustedQuotas
                        };

                        if (exhaustedQuotas > 0)
                        {
                            healthData[$"{provider}_has_exhausted_quotas"] = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to check quotas for provider {Provider}", provider);
                    }
                }

                healthData["provider_quotas"] = quotaStatuses;
            }

            var isHealthy = unhealthyReasons.Count == 0;
            var status = isHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy;
            var description = isHealthy
                ? $"AI Orchestration is healthy. {healthyProviders}/{totalProviders} providers healthy."
                : $"AI Orchestration issues: {string.Join("; ", unhealthyReasons)}";

            return new HealthCheckResult(status, description, data: healthData);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Health check failed");

            return new HealthCheckResult(
                HealthStatus.Unhealthy,
                $"Health check threw an exception: {ex.Message}",
                ex,
                healthData);
        }
    }
}