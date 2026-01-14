namespace DictionaryImporter.AI.Core.Exceptions;

public class ProviderQuotaExceededException : AiOrchestrationException
{
    public string ProviderName { get; }

    public ProviderQuotaExceededException(string providerName, string message = null)
        : base(
            message ?? $"Quota exceeded for provider: {providerName}",
            $"QUOTA_EXCEEDED_{providerName.ToUpper()}",
            false)
    {
        ProviderName = providerName;
    }
}