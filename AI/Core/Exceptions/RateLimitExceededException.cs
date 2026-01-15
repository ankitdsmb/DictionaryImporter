namespace DictionaryImporter.AI.Core.Exceptions;

public class RateLimitExceededException(
    string providerName,
    TimeSpan retryAfter,
    string message = null)
    : AiOrchestrationException(
        message ?? $"Rate limit exceeded for {providerName}. Retry after {retryAfter.TotalSeconds} seconds",
        $"RATE_LIMIT_{providerName.ToUpper()}",
        true)
{
    public string ProviderName { get; } = providerName;
    public TimeSpan RetryAfter { get; } = retryAfter;
}