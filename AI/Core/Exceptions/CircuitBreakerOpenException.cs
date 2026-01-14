namespace DictionaryImporter.AI.Core.Exceptions;

public class CircuitBreakerOpenException : AiOrchestrationException
{
    public string ProviderName { get; }
    public TimeSpan Duration { get; }

    public CircuitBreakerOpenException(string providerName, TimeSpan duration)
        : base(
            $"Circuit breaker is open for {providerName}. Will retry after {duration.TotalSeconds} seconds",
            $"CIRCUIT_BREAKER_{providerName.ToUpper()}",
            true)
    {
        ProviderName = providerName;
        Duration = duration;
    }
}