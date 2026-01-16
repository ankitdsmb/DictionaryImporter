namespace DictionaryImporter.AITextKit.AI.Infrastructure.HealthChecks;

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