using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Diagnostics;

namespace DictionaryImporter.AI.Extensions;

public static class HealthCheckBuilderExtensions
{
    public static IHealthChecksBuilder AddMemoryHealthCheck(
        this IHealthChecksBuilder builder,
        string name,
        long maximumMemoryBytes,
        HealthStatus? failureStatus = null,
        IEnumerable<string> tags = null)
    {
        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new MemoryHealthCheck(maximumMemoryBytes),
            failureStatus,
            tags));
    }
}

public class MemoryHealthCheck(long maximumMemoryBytes) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var memoryInfo = GC.GetGCMemoryInfo();
            var totalMemoryBytes = memoryInfo.TotalAvailableMemoryBytes;
            var memoryUsedBytes = Process.GetCurrentProcess().WorkingSet64;
            var memoryPercentage = totalMemoryBytes > 0 ?
                (double)memoryUsedBytes / totalMemoryBytes * 100 : 0;

            var data = new Dictionary<string, object>
            {
                ["total_memory_bytes"] = totalMemoryBytes,
                ["memory_used_bytes"] = memoryUsedBytes,
                ["memory_percentage"] = memoryPercentage,
                ["maximum_allowed_bytes"] = maximumMemoryBytes
            };

            if (memoryUsedBytes > maximumMemoryBytes)
            {
                return Task.FromResult(
                    HealthCheckResult.Unhealthy(
                        $"Memory usage exceeded: {memoryUsedBytes / 1024 / 1024}MB > {maximumMemoryBytes / 1024 / 1024}MB",
                        data: data));
            }

            if (memoryPercentage > 90)
            {
                return Task.FromResult(
                    HealthCheckResult.Degraded(
                        $"Memory usage is high: {memoryPercentage:F1}%",
                        data: data));
            }

            return Task.FromResult(
                HealthCheckResult.Healthy(
                    $"Memory usage OK: {memoryPercentage:F1}%",
                    data: data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                HealthCheckResult.Unhealthy(
                    "Failed to check memory usage",
                    ex,
                    new Dictionary<string, object> { ["error"] = ex.Message }));
        }
    }
}