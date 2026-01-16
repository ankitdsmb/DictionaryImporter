namespace DictionaryImporter.AITextKit.AI.Infrastructure.HealthChecks;

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