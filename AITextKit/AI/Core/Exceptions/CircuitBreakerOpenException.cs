namespace DictionaryImporter.AITextKit.AI.Core.Exceptions;

public class CircuitBreakerOpenException(string providerName, TimeSpan duration) : AiOrchestrationException(
    $"Circuit breaker is open for {providerName}. Will retry after {duration.TotalSeconds} seconds",
    $"CIRCUIT_BREAKER_{providerName.ToUpper()}",
    true)
{
    public string ProviderName { get; } = providerName;
    public TimeSpan Duration { get; } = duration;
}