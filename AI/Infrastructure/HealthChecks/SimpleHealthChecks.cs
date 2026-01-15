using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.AI.Infrastructure.HealthChecks;

public class DatabaseHealthCheck(string connectionString, ILogger<DatabaseHealthCheck> logger)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            var result = await command.ExecuteScalarAsync(cancellationToken);

            if (result?.ToString() == "1")
            {
                return HealthCheckResult.Healthy("Database connection successful");
            }

            return HealthCheckResult.Unhealthy("Database query failed");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Database health check failed");
            return HealthCheckResult.Unhealthy("Database connection failed", ex);
        }
    }
}

public class UrlHealthCheck(Uri url, TimeSpan timeout, ILogger<UrlHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new HttpClient();
            client.Timeout = timeout;

            var response = await client.GetAsync(url, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Healthy($"URL {url} is reachable");
            }

            return HealthCheckResult.Degraded(
                $"URL {url} returned status code: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "URL health check failed for {Url}", url);
            return HealthCheckResult.Unhealthy($"URL {url} is not reachable", ex);
        }
    }
}