namespace DictionaryImporter.AITextKit.AI.Extensions;

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