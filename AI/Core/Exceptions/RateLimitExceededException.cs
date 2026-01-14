namespace DictionaryImporter.AI.Core.Exceptions;

public class RateLimitExceededException : AiOrchestrationException
{
    public string ProviderName { get; }
    public TimeSpan RetryAfter { get; }

    public RateLimitExceededException(
        string providerName,
        TimeSpan retryAfter,
        string message = null)
        : base(
            message ?? $"Rate limit exceeded for {providerName}. Retry after {retryAfter.TotalSeconds} seconds",
            $"RATE_LIMIT_{providerName.ToUpper()}",
            true)
    {
        ProviderName = providerName;
        RetryAfter = retryAfter;
    }
}